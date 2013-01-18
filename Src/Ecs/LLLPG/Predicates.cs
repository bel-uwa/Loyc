﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using ecs;
using Loyc.Essentials;
using Loyc.Utilities;
using Loyc.CompilerCore;

namespace Loyc.LLParserGenerator
{
	/// <summary>Represents part of a grammar for the <see cref="LLParserGenerator"/>.</summary>
	/// <remarks>
	/// This class is the root of a class hierarchy which contains
	/// <ul>
	/// <li><see cref="TerminalSet"/>: represents a terminal (which is a token or a 
	///     character) or a set of possible terminals (e.g. 'A'..'Z'). This class 
	///     has subclasses including <see cref="CharSet"/> and <see cref="AnyTerminal"/>.</li>
	/// <li><see cref="RuleRef"/>: represents a nonterminal, which is a reference to a rule.</li>
	/// <li>Other components of a rule:
	///     terminals and nonterminals (<see cref="TerminalSet"/> and <see cref="RuleRef"/>), 
	///     sequences (<see cref="Seq"/>),
	///     branches and loops (<see cref="Alts"/>),
	///     gates (<see cref="Gate"/>, a mechanism to separate prediction from matching), and
	///     and-predicates (<see cref="AndPred"/>, an assertion that consumes no input).</li>
	/// <li><see cref="EndOfRule"/>: a container for the follow set of a <see cref="Rule"/> 
	///     (this class is not a real predicate; it is derived from Pred so that it 
	///     can be a legal value for <see cref="Pred.Next"/>).</li>
	/// </remarks>
	public abstract class Pred
	{
		public abstract void Call(PredVisitor visitor); // visitor pattern

		public Pred(Node basis) { Basis = basis ?? Node.Missing; }

		public readonly Node Basis;
		public Node PreAction;
		public Node PostAction;
		public Pred Next; // The predicate that follows this one or EndOfRule

		public static Node AppendAction(Node action, Node action2)
		{
			if (action == null)
				return action2;
			else {
				action = action.Unfrozen();
				// TODO: implement ArgList.AddRange()
				int at = action.Args.Count;
				for (int j = action2.ArgCount-1; j >= 0; j--)
					action.Args.Insert(at, action2.Args.Detach(j));
				return action;
			}
		}

		public abstract bool IsNullable { get; }

		// Helper methods for creating a grammar without a source file (this is
		// used for testing and for bootstrapping the parser generator).
		public static Seq  operator + (char a, Pred b) { return Char(a) + b; }
		public static Seq  operator + (Pred a, char b) { return a + Char(b); }
		public static Seq  operator + (Pred a, Pred b) { return new Seq(a, b); }
		public static Pred operator | (char a, Pred b) { return Char(a) | b; }
		public static Pred operator | (Pred a, char b) { return a | Char(b); }
		public static Pred operator | (Pred a, Pred b) { return Or(a, b, false); }
		public static Pred operator / (Pred a, Pred b) { return Or(a, b, true); }
		public static Pred Or(Pred a, Pred b, bool ignoreAmbig)
		{
			TerminalPred a_ = a as TerminalPred, b_ = b as TerminalPred;
			if (a_ != null && b_ != null && a_.CanMerge(b_))
				return a_.Merge(b_);
			else
				return new Alts(null, a, b, ignoreAmbig);
		}
		public static Alts Star (Pred contents) { return new Alts(null, LoopMode.Star, contents); }
		public static Alts Opt (Pred contents) { return new Alts(null, LoopMode.Opt, contents); }
		public static Seq Plus (Pred contents) { return contents + new Alts(null, LoopMode.Star, contents.Clone()); }
		public static TerminalPred Range(char lo, char hi) { return new TerminalPred(null, lo, hi); }
		public static TerminalPred Set(IPGTerminalSet set) { return new TerminalPred(null, set); }
		public static TerminalPred Char(char c) { return new TerminalPred(null, c); }
		public static TerminalPred Chars(params char[] c)
		{
			var set = PGIntSet.WithChars(c.Select(ch => (int)ch).ToArray());
			return new TerminalPred(null, set);
		}
		public static Rule Rule(string name, Pred pred, bool isStartingRule = false, bool isToken = false, int maximumK = -1)
		{
			return new Rule(null, GSymbol.Get(name), pred, isStartingRule) { IsToken = isToken, K = maximumK };
		}
		/// <summary>Deep-clones a predicate tree. Terminal sets and Nodes 
		/// referenced by the tree are not cloned; the clone's value of
		/// <see cref="Next"/> will be null. The same <see cref="Pred"/> cannot 
		/// appear in two places in a tree, so you must clone before re-use.</summary>
		public virtual Pred Clone()
		{
			var clone = (Pred)MemberwiseClone();
			clone.Next = null;
			return clone;
		}
	}
	/// <summary>Represents a nonterminal, which is a reference to a rule.</summary>
	public class RuleRef : Pred
	{
		public override void Call(PredVisitor visitor) { visitor.Visit(this); }
		public RuleRef(Node basis, Rule rule) : base(basis) { Rule = rule; }
		public new Rule Rule;
		public override bool IsNullable
		{
			get { return Rule.Pred.IsNullable; }
		}
	}
	
	/// <summary>Represents a sequence of predicates (<see cref="Pred"/>s).</summary>
	public class Seq : Pred
	{
		public override void Call(PredVisitor visitor) { visitor.Visit(this); }
		public Seq(Node basis) : base(basis) {}
		public Seq(Pred one, Pred two) : base(null)
		{
			if (one is Seq)
				List.AddRange((one as Seq).List);
			else
				List.Add(one);
			Debug.Assert(!(two is Seq));
			List.Add(two);
		}
		public List<Pred> List = new List<Pred>();

		public override bool IsNullable
		{
			get { return List.TrueForAll(p => p.IsNullable); }
		}
		public override Pred Clone()
		{
			Seq clone = (Seq)base.Clone();
			clone.List = new List<Pred>(List.Count);
			for (int i = 0; i < List.Count; i++)
				clone.List[i] = List[i].Clone();
			return clone;
		}
	}
	
	/// <summary>Describes a series of alternatives (branches), a kleene star 
	/// (`*`), or an optional element (`?`).</summary>
	/// <remarks>
	/// Branches, stars and optional elements are represented by the same class 
	/// because they all require prediction, and prediction works the same way for 
	/// all three.
	/// <para/>
	/// The one-or-more operator '+' is represented simply by repeating the 
	/// contents once, i.e. (x+) is converted to (x x*), which is a Seq of
	/// two elements: x and an Alts object that contains x.
	/// </remarks>
	public class Alts : Pred
	{
		public override void Call(PredVisitor visitor) { visitor.Visit(this); }

		public Alts(Node basis, LoopMode mode, bool ignoreAmbig) : base(basis)
		{
			Mode = mode;
			IgnoreAmbiguous = ignoreAmbig;
		}
		public Alts(Node basis, Pred a, Pred b, bool ignoreAmbig = false) : this(basis, LoopMode.None, ignoreAmbig)
		{
			Add(a);
			Add(b);
		}
		public Alts(Node basis, LoopMode mode, Pred contents) : this(basis, mode, false)
		{
			Debug.Assert(mode == LoopMode.Star || mode == LoopMode.Opt);
			var contents2 = contents as Alts;
			if (contents2 != null) {
				if (contents2.Mode == LoopMode.Opt || contents2.Mode == LoopMode.Star)
					throw new ArgumentException(Localize.From("{0} predicate cannot directly contain {1} predicate", ToStr(mode), ToStr(contents2.Mode)));
				IgnoreAmbiguous = contents2.IgnoreAmbiguous;
				Greedy = contents2.Greedy;
				Arms = contents2.Arms;
			} else {
				Arms.Add(contents);
			}
		}
		static string ToStr(LoopMode m) 
		{
			switch(m) {
				case LoopMode.Opt: return "an optional (?)";
				case LoopMode.Star: return "a loop (*, +)";
				default: return "an alternative list";
			}
		}
		
		public LoopMode Mode = LoopMode.None;
		public bool IgnoreAmbiguous = false;
		public bool Greedy = false;
		public List<Pred> Arms = new List<Pred>();
		public int DefaultArm = -1;
		public bool HasExit { get { return Mode != LoopMode.None; } }
		public int ArmCountPlusExit
		{
			get { return Arms.Count + (HasExit ? 1 : 0); }
		}

		public void Add(Pred p)
		{
			var a = p as Alts;
			if (a != null && a.Mode == LoopMode.None && a.IgnoreAmbiguous == IgnoreAmbiguous)
				Arms.AddRange(a.Arms);
			else
				Arms.Add(p);
		}

		public override bool IsNullable
		{
			get {
				if (Mode != LoopMode.None)
					return true;
				return Arms.Any(arm => arm.IsNullable);
			}
		}
		
		public override Pred Clone()
		{
			Alts clone = (Alts)base.Clone();
			clone.Arms = new List<Pred>(Arms.Count);
			for (int i = 0; i < Arms.Count; i++)
				clone.Arms[i] = Arms[i].Clone();
			return clone;
		}
	}
	/// <summary>Types of <see cref="Alts"/> objects.</summary>
	/// <remarks>Although x? can be simulated with (x|), we keep them as separate modes for reporting purposes.</remarks>
	public enum LoopMode { None, Opt, Star };

	/// <summary>Represents a "gate" (p => m), which is a mechanism to separate 
	/// prediction from matching in the context of branching (<see cref="Alts"/>).</summary>
	public class Gate : Pred
	{
		public override void Call(PredVisitor visitor) { visitor.Visit(this); }
		public Gate(Node basis, Pred predictor, Pred match) : base(basis) {
			G.Require(!(predictor is Gate) && !(match is Gate),
				"A gate '=>' cannot contain another gate");
			_predictor = predictor;
			_match = match;
		}
		Pred _predictor;
		Pred _match;
		public Pred Predictor { get { return _predictor; } }
		public Pred Match { get { return _match; } }

		public override bool IsNullable
		{
			get { return Predictor.IsNullable; }
		}
		public override Pred Clone()
		{
			Gate clone = (Gate)base.Clone();
			clone._predictor = _predictor.Clone();
			clone._match = _match.Clone();
			return clone;
		}
	}

	/// <summary>Represents a zero-width assertion: either user-defined code to
	/// check a condition, or a predicate that scans ahead in the input and then
	/// backtracks to the starting point.</summary>
	public class AndPred : Pred
	{
		public override void Call(PredVisitor visitor) { visitor.Visit(this); }
		public AndPred(Node basis, object pred, bool not) : base(basis) { Pred = pred; Not = not; }
		
		/// <summary>Inverts the condition if Not==true, so that if the 
		/// <see cref="Pred"/> matches, the <see cref="AndPred"/> does not 
		/// match, and vice versa.</summary>
		public bool Not = false;
		
		/// <summary>The predicate to match and backtrack. Must be of type 
		/// <see cref="Node"/> or <see cref="Pred"/>.</summary>
		public object Pred;

		public override bool IsNullable
		{
			get { return true; }
		}
		public override Pred Clone()
		{
			return base.Clone();
		}
	}

	/// <summary>Represents a terminal (which is a token or a character) or a set 
	/// of possible terminals (e.g. 'A'..'Z').</summary>
	public class TerminalPred : Pred
	{
		public override void Call(PredVisitor visitor) { visitor.Visit(this); }
		public static TerminalPred AnyFollowSet()
		{
			var a = new TerminalPred(null, TrivialTerminalSet.All());
			a.Next = a;
			return a;
		}
		
		new public IPGTerminalSet Set;

		public TerminalPred(Node basis, char ch) : base(basis) { Set = new PGIntSet(new IntRange(ch), true); }
		public TerminalPred(Node basis, int ch) : base(basis) { Set = new PGIntSet(new IntRange(ch), false); }
		public TerminalPred(Node basis, char lo, char hi) : base(basis) { Set = new PGIntSet(new IntRange(lo, hi), true); }
		
		/// <summary>Initializes the object with the specified set. If the set 
		/// contains EOF (usually because it is inverted), EOF is removed from 
		/// the set because EOF is not a terminal. If the parser generator is 
		/// given a <see cref="TerminalPred"/> that includes EOF, the generated 
		/// code will almost certainly be wrong.</summary>
		public TerminalPred(Node basis, IPGTerminalSet set) : base(basis) 
		{
			if ((Set = set).ContainsEOF)
				set.ContainsEOF = false;
		}

		// For combining with | operator; cannot merge if PreAction/PostAction differs between arms
		public virtual bool CanMerge(TerminalPred r)
		{
			return r.PreAction == PreAction && r.PostAction == PostAction;
		}
		public TerminalPred Merge(TerminalPred r, bool ignoreActions = false)
		{
			if (!ignoreActions && (PreAction != r.PreAction || PostAction != r.PostAction))
				throw new InvalidOperationException("Internal error: cannot merge TerminalPreds that have actions");
			return new TerminalPred(Basis, Set.Union(r.Set)) { PreAction = PreAction, PostAction = PostAction };
		}

		public override bool IsNullable
		{
			get { return false; }
		}
		public override string ToString() // for debugging
		{
			return Set.ToString();
		}
	}

	/// <summary>A container for the follow set of a <see cref="Rule"/>.</summary>
	public class EndOfRule : Pred
	{
		public EndOfRule() : base(null) { }
		public override void Call(PredVisitor visitor) { visitor.Visit(this); }
		public HashSet<Pred> FollowSet = new HashSet<Pred>();

		public override bool IsNullable
		{
			get { throw new NotImplementedException(); }
		}
	}
}