﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using Loyc;
using Loyc.Collections;
using Loyc.Collections.Impl;
using Loyc.Threading;
using Loyc.Utilities;
using Loyc.Syntax;
using Loyc.LLParserGenerator;
using Loyc.Syntax.Lexing;
using Loyc.Syntax.Les;

namespace Ecs.Parser
{
	using TT = TokenType;

	/// <summary>Lexer for EC# source code (see <see cref="ILexer"/>).</summary>
	/// <seealso cref="WhitespaceFilter"/>
	/// <seealso cref="TokensToTree"/>
	public partial class EcsLexer : BaseLexer, ILexer
	{
		public EcsLexer(string text, IMessageSink sink) : base(new StringSlice(text), "") { ErrorSink = sink; }
		public EcsLexer(ICharSource text, string fileName, IMessageSink sink, int startPosition = 0) : base(text, fileName, startPosition) { ErrorSink = sink; }

		public bool AllowNestedComments = false;
		private bool _isFloat, _parseNeeded, _isNegative, _verbatim;
		// Alternate: hex numbers, verbatim strings
		// UserFlag: bin numbers, double-verbatim
		private NodeStyle _style;
		private int _numberBase;
		private Symbol _typeSuffix;
		private TokenType _type; // predicted type of the current token
		private object _value;
		private int _startPosition;
		// _allowPPAt is used to detect whether a preprocessor directive is allowed
		// at the current input position. When _allowPPAt==_startPosition, it's allowed.
		private int _allowPPAt;

		new public void Reset(ICharSource source, string fileName = "", int inputPosition = 0)
		{
			base.Reset(source, fileName, inputPosition, true);
		}

		public new ISourceFile SourceFile { get { return base.SourceFile; } }
		public IMessageSink ErrorSink { get; set; }

		int _indentLevel;
		UString _indent;
		public int IndentLevel { get { return _indentLevel; } }
		public int SpacesPerTab = 4;

		public Token? NextToken()
		{
			_startPosition = InputPosition;
			_value = null;
			_style = 0;
			if (InputPosition >= CharSource.Count)
				return null;
			else {
				Token();
				Debug.Assert(InputPosition > _startPosition);
				return new Token((int)_type, _startPosition, InputPosition - _startPosition, _style, _value);
			}
		}

		protected override void Error(int index, string message)
		{
			// the fast "blitting" code path may not be able to handle errors
			_parseNeeded = true;

			var pos = SourceFile.IndexToLine(index);
			if (ErrorSink != null)
				ErrorSink.Write(Severity.Error, pos, message);
			else
				throw new FormatException(pos + ": " + message);
		}
				
		public void Restart()
		{
			_indentLevel = 0;
			_lineNumber = 0;
			_allowPPAt = _lineStartAt = 0;
		}

		internal static readonly HashSet<Symbol> CsKeywords = EcsNodePrinter.CsKeywords;
		internal static readonly HashSet<Symbol> PreprocessorIdentifiers = EcsNodePrinter.SymbolSet(
			"if", "else", "elif", "endif", "define", "undef", "line", 
			"region", "endregion", "warning", "error", "note");

		// This is the set of keywords that act only as attributes on statements.
		// This list does not include "new" and "out", which are only allowed as 
		// attributes on variable declarations and other specific statements.
		static readonly HashSet<Symbol> AttrKeywords = EcsNodePrinter.SymbolSet(
			"abstract", "const", "explicit", "extern", "implicit", "internal", //"new",
			"override", "params", "private", "protected", "public", "readonly", "ref",
			"sealed", "static", "unsafe", "virtual", "volatile");

		static readonly HashSet<Symbol> TypeKeywords = EcsNodePrinter.SymbolSet(
			"bool", "byte", "char", "decimal", "double", "float", "int", "long",
			"object", "sbyte", "short", "string", "uint", "ulong", "ushort", "void");

		// contains non-trivial mappings like int => #int32. If the string is not in 
		// this map, we simply add "#" on the front to form the token's Value.
		static readonly Dictionary<string, Symbol> TokenNameMap = InverseMap(EcsNodePrinter.TypeKeywords);
		private static Dictionary<K,V> InverseMap<K,V>(IEnumerable<KeyValuePair<V,K>> list)
		{
			var d = new Dictionary<K, V>();
			foreach (var pair in list)
				d.Add(pair.Value, pair.Key);
			return d;
		}

		#region Value parsers
		// After the generated lexer code determines the boundaries of the token, 
		// one of these methods extracts the value of the token (e.g. "17L" => (long)17)
		// There are value parsers for identifiers, numbers, and strings; certain
		// parser cores are also accessible as public static methods.

		#region String parsing

		void ParseSQStringValue()
		{
			int len = InputPosition - _startPosition;
			if (!_parseNeeded && len == 3) {
				_value = CG.Cache(CharSource[_startPosition + 1]);
			} else {
				string s = ParseStringCore(_startPosition);
				_value = s;
				if (s.Length == 1)
					_value = CG.Cache(s[0]);
				else if (s.Length == 0)
					Error(_startPosition, Localize.From("Empty character literal"));
				else
					Error(_startPosition, Localize.From("Character literal has {0} characters (there should be exactly one)", s.Length));
			}
		}

		void ParseBQStringValue()
		{
			var value = ParseStringCore(_startPosition);
			_value = GSymbol.Get(value.ToString());
		}

		void ParseStringValue()
		{
			_value = ParseStringCore(_startPosition);
			if (_value.ToString().Length < 16)
				_value = CG.Cache(_value);
		}

		string ParseStringCore(int start)
		{
			Debug.Assert(_verbatim == (CharSource[start] == '@'));
			if (_verbatim)
				start++;
			char q;
			Debug.Assert((q = CharSource.TryGet(start, '\0')) == '"' || q == '\'' || q == '`');
			bool tripleQuoted = (_style & NodeStyle.Alternate2) != 0;

			string value;
			if (!_parseNeeded) {
				Debug.Assert(!tripleQuoted);
				value = (string)CharSource.Slice(start + 1, InputPosition - start - 2).ToString();
			} else {
				UString original = CharSource.Slice(start, InputPosition - start);
				value = UnescapeQuotedString(ref original, _verbatim, Error, _indent);
			}
			return value;
		}

		static string UnescapeQuotedString(ref UString source, bool isVerbatim, Action<int, string> onError, UString indentation)
		{
			Debug.Assert(source.Length >= 1);
			if (isVerbatim) {
				bool fail;
				char stringType = (char)source.PopFront(out fail);
				StringBuilder sb = new StringBuilder();
				int c;
				for (;;) {
					c = source.PopFront(out fail);
					if (fail) break;
					if (c == stringType) {
						if ((c = source.PopFront(out fail)) != stringType)
							break;
					}
					sb.Append((char)c);
				}
				return sb.ToString();
			} else {
				// triple-quoted or normal string: let LES lexer handle it
				return LesLexer.UnescapeQuotedString(ref source, onError, indentation);
			}
		}

		#endregion

		#region Identifier & Symbol parsing (including public ParseIdentifier())

		// id & symbol cache. For Symbols, includes only one of the two @ signs.
		protected Dictionary<UString, object> _idCache = new Dictionary<UString, object>();

		void ParseIdValue(int skipAt, bool isBQString)
		{
			ParseIdOrSymbol(_startPosition + skipAt, isBQString);
		}
		void ParseSymbolValue(bool isBQString)
		{
			ParseIdOrSymbol(_startPosition + 2, isBQString);
		}

		void ParseIdOrSymbol(int start, bool isBQString)
		{
			UString unparsed = CharSource.Slice(start, InputPosition - start);
			UString parsed;
			Debug.Assert(isBQString == (CharSource.TryGet(start, '\0') == '`'));
			Debug.Assert(!_verbatim);
			if (!_idCache.TryGetValue(unparsed, out _value)) {
				if (isBQString)
					parsed = ParseStringCore(start);
				else if (_parseNeeded)
					parsed = ScanNormalIdentifier(unparsed);
				else
					parsed = unparsed;
				_idCache[unparsed.ShedExcessMemory(50)] = _value = GSymbol.Get(parsed.ToString());
			}
		}

		static string ScanNormalIdentifier(UString text)
		{
			var parsed = new StringBuilder();
			char c;
			while ((c = text[0, '\0']) != '\0') {
				if (!ScanUnicodeEscape(ref text, parsed, c)) {
					parsed.Append(c);
					text = text.Slice(1);
				}
			}
			return parsed.ToString();
		}
		static bool ScanUnicodeEscape(ref UString text, StringBuilder parsed, char c)
		{
			// I can't imagine why this exists in C# in the first place. Unicode 
			// escapes inside identifiers are required to be letters or digits,
			// although my lexer doesn't enforce this (EC# needs no such rule.)
			if (c != '\\')
				return false;
			char u = text.TryGet(1, '\0');
			int len = 4;
			if (u == 'u' || u == 'U') {
				if (u == 'U') len = 8;
				if (text.Length < 2 + len)
					return false;

				var digits = text.Substring(2, len);
				int code;
				if (G.TryParseHex(digits, out code) && code <= 0x0010FFFF) {
					if (code >= 0x10000) {
						parsed.Append((char)(0xD800 + ((code - 0x10000) >> 10)));
						parsed.Append((char)(0xDC00 + ((code - 0x10000) & 0x3FF)));
					} else
						parsed.Append((char)code);
					text = text.Substring(2 + len);
					return true;
				}
			}
			return false;
		}









		//private bool FindCurrentIdInKeywordTrie(Trie t, string source, int start, ref Symbol value, ref TokenType type)
		//{
		//    Debug.Assert(InputPosition >= start);
		//    for (int i = start, stop = InputPosition; i < stop; i++) {
		//        char input = source[i];
		//        int input_i = input - t.CharOffs;
		//        if (t.Child == null || (uint)input_i >= t.Child.Length) {
		//            if (input == '\'' && t.Value != null) {
		//                // Detected keyword followed by single quote. This requires 
		//                // the lexer to backtrack so that, for example, case'x' is 
		//                // treated as two tokens instead of the one token it 
		//                // initially appears to be.
		//                InputPosition = i;
		//                break;
		//            }
		//            return false;
		//        }
		//        if ((t = t.Child[input - t.CharOffs]) == null)
		//            return false;
		//    }
		//    if (t.Value != null) {
		//        value = t.Value;
		//        type = t.TokenType;
		//        return true;
		//    }
		//    return false;
		//}


		#endregion

		#region Number parsing

		static Symbol _sub = GSymbol.Get("-");
		static Symbol _F = GSymbol.Get("F");
		static Symbol _D = GSymbol.Get("D");
		static Symbol _M = GSymbol.Get("M");
		static Symbol _U = GSymbol.Get("U");
		static Symbol _L = GSymbol.Get("L");
		static Symbol _UL = GSymbol.Get("UL");

		void ParseNumberValue()
		{
			int start = _startPosition;
			if (_isNegative)
				start++;
			if (_numberBase != 10)
				start += 2;
			int stop = InputPosition;
			if (_typeSuffix != null)
				stop -= _typeSuffix.Name.Length;

			UString digits = CharSource.Slice(start, stop - start);
			string error;
			if ((_value = LesLexer.ParseNumberCore(digits, _isNegative, _numberBase, _isFloat, _typeSuffix, out error)) == null)
				_value = 0;
			else if (_value == CodeSymbols.Sub) {
				InputPosition = _startPosition + 1;
				_type = TT.Sub;
			}
			if (error != null)
				Error(_startPosition, error);
		}

		#endregion

		#endregion

		// Due to the way generics are implemented, repeating the implementation 
		// of this base-class method might improve performance (TODO: verify this idea)
		new protected int LA(int i)
		{
			bool fail;
			char result = CharSource.TryGet(InputPosition + i, out fail);
			return fail ? -1 : result;
		}

		int MeasureIndent(UString indent)
		{
			return LesLexer.MeasureIndent(indent, SpacesPerTab);
		}

		Token? _current;

		void IDisposable.Dispose() {}
		Token IEnumerator<Token>.Current { get { return _current.Value; } }
		object System.Collections.IEnumerator.Current { get { return _current; } }
		void System.Collections.IEnumerator.Reset() { throw new NotSupportedException(); }
		bool System.Collections.IEnumerator.MoveNext()
		{
			_current = NextToken();
			return _current.HasValue;
		}
	}

}
