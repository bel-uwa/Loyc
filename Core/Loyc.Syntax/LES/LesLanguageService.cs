﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Loyc.Utilities;
using Loyc.Syntax.Lexing;
using Loyc.Collections;

namespace Loyc.Syntax.Les
{
	/// <summary>The <see cref="Value"/> property provides easy access to the lexer, 
	/// parser and printer for Loyc Expression Syntax (LES).</summary>
	/// <remarks>
	/// LES overview: http://sourceforge.net/apps/mediawiki/loyc/index.php?title=LES
	/// </remarks>
	public class LesLanguageService : IParsingService
	{
		public static readonly LesLanguageService Value = new LesLanguageService();

		public override string ToString()
		{
			return "Loyc Expression Syntax (alpha)";
		}

		static readonly string[] _fileExtensions = new[] { "les" };
		public IEnumerable<string> FileExtensions { get { return _fileExtensions; } }

		public LNodePrinter Printer
		{
			get { return LesNodePrinter.Printer; }
		}
		public string Print(LNode node, IMessageSink msgs, object mode = null, string indentString = "\t", string lineSeparator = "\n")
		{
			var sb = new StringBuilder();
			Printer(node, sb, msgs, mode, indentString, lineSeparator);
			return sb.ToString();
		}
		public bool HasTokenizer
		{
			get { return true; }
		}
		public ILexer Tokenize(ICharSource text, string fileName, IMessageSink msgs)
		{
			return new LesLexer(text, fileName, msgs);
		}
		public IListSource<LNode> Parse(ICharSource text, string fileName, IMessageSink msgs, Symbol inputType = null)
		{
			var lexer = Tokenize(text, fileName, msgs);
			return Parse(lexer, msgs, inputType);
		}
		public IListSource<LNode> Parse(ILexer input, IMessageSink msgs, Symbol inputType = null)
		{
			var treeified = new TokensToTree(input, true);
			return Parse(treeified.Buffered(), input.SourceFile, msgs, inputType);
		}

		[ThreadStatic]
		static LesParser _parser;

		public IListSource<LNode> Parse(IListSource<Token> input, ISourceFile file, IMessageSink msgs, Symbol inputType = null)
		{
			// For efficiency we'd prefer to re-use our _parser object, but
			// when parsing lazily, we can't re-use it because another parsing 
			// operation could start before this one is finished. To force 
			// greedy parsing, we can call ParseStmtsGreedy(), but the caller may 
			// prefer lazy parsing, especially if the input is large. As a 
			// compromise I'll check if the source file is larger than a 
			// certain arbitrary size. Also, ParseExprs() is always greedy 
			// so we can always re-use _parser in that case.
			bool exprMode = inputType == ParsingService.Exprs;
			char _ = '\0';
			if (inputType == ParsingService.Exprs || file.Text.TryGet(255, ref _)) {
				LesParser parser = _parser;
				if (parser == null)
					_parser = parser = new LesParser(input, file, msgs);
				else {
					parser.MessageSink = msgs;
					parser.Reset(input, file);
				}
				if (inputType == ParsingService.Exprs)
					return parser.ParseExprs();
				else
					return parser.ParseStmtsGreedy();
			} else {
				var parser = new LesParser(input, file, msgs);
				return parser.ParseStmtsLazy().Buffered();
			}
		}
	}
}
