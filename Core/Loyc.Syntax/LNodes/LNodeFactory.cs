using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Loyc.Utilities;
using System.Diagnostics;
using Loyc.Collections;
using Loyc.Collections.MutableListExtensionMethods;
using S = Loyc.Syntax.CodeSymbols;
using Loyc.Syntax.Lexing;
using static System.Math;

namespace Loyc.Syntax
{
	/// <summary>Contains helper methods for creating <see cref="LNode"/>s.
	/// An LNodeFactory holds a reference to the current source file (<see cref="File"/>) 
	/// so that it does not need to be repeated every time you create a node.
	/// </summary>
	public class LNodeFactory
	{
		public static readonly LNode Missing_ = new StdIdNode(S.Missing, new SourceRange(EmptySourceFile.Unknown));
		
		private LNode _emptyList, _emptySplice, _emptyTuple;
		public LNode Missing { get { return Missing_; } } // allow access through class reference

		ISourceFile _file;
		public ISourceFile File { get { return _file; } set { _file = value; } }
		
		IMessageSink<LNode> _errorSink;
		/// <summary>Where errors should be sent if there is an error parsing a literal.</summary>
		/// <remarks>Attempting to set this to null makes the getter return <see cref="MessageSink.Default"/>.</remarks>
		public IMessageSink<LNode> ErrorSink
		{
			get => _errorSink ?? MessageSink.Default;
			set => _errorSink = value;
		}

		public LNodeFactory() : this(EmptySourceFile.Unknown) { }
		public LNodeFactory(ISourceFile file, IMessageSink sink = null)
		{
			_file = file;
			ErrorSink = sink;
		}

		#region Common literals, data types and access modifiers

		// Common literals
		public LNode @true { get { return Literal(true); } }
		public LNode @false { get { return Literal(false); } }
		public LNode @null { get { return Literal((object)null); } }
		public LNode @void { get { return Literal(@void.Value); } }
		public LNode int_0 { get { return Literal(0); } }
		public LNode int_1 { get { return Literal(1); } }
		public LNode string_empty { get { return Literal(""); } }

		public LNode @this { get { return Id(S.This); } }
		public LNode @base { get { return Id(S.Base); } }

		public LNode DefKeyword { get { return Id(S.Fn, -1); } }

		// Standard data types (marked synthetic)
		public LNode Void { get { return Id(S.Void, -1); } }
		public LNode String { get { return Id(S.String, -1); } }
		public LNode Object { get { return Id(S.Object, -1); } }
		public LNode Char { get { return Id(S.Char, -1); } }
		public LNode Bool { get { return Id(S.Bool, -1); } }
		public LNode Int8 { get { return Id(S.Int8, -1); } }
		public LNode Int16 { get { return Id(S.Int16, -1); } }
		public LNode Int32 { get { return Id(S.Int32, -1); } }
		public LNode Int64 { get { return Id(S.Int64, -1); } }
		public LNode UInt8 { get { return Id(S.UInt8, -1); } }
		public LNode UInt16 { get { return Id(S.UInt16, -1); } }
		public LNode UInt32 { get { return Id(S.UInt32, -1); } }
		public LNode UInt64 { get { return Id(S.UInt64, -1); } }
		public LNode Single { get { return Id(S.Single, -1); } }
		public LNode Double { get { return Id(S.Double, -1); } }
		public LNode Decimal { get { return Id(S.Decimal, -1); } }

		// Standard access modifiers
		public LNode Internal { get { return Id(S.Internal, -1); } }
		public LNode Public { get { return Id(S.Public, -1); } }
		public LNode ProtectedIn { get { return Id(S.ProtectedIn, -1); } }
		public LNode Protected { get { return Id(S.Protected, -1); } }
		public LNode Private { get { return Id(S.Private, -1); } }

		public LNode True { get { return Literal(true); } }
		public LNode False { get { return Literal(false); } }
		public LNode Null { get { return Literal((object)null); } }

		LNode _newline = null;
		public LNode TriviaNewline { get { return _newline = _newline ?? Id(S.TriviaNewline); } }
		
		/// <summary>Adds a leading newline to the node if the first attribute isn't a newline.</summary>
		/// <remarks>By convention, in Loyc languages, top-level nodes and nodes within 
		/// braces have an implicit newline, such that a leading blank line appears
		/// if you add <see cref="CodeSymbols.TriviaNewline"/>. For all other nodes,
		/// this method just ensures there is a line break.</remarks>
		public LNode OnNewLine(LNode node)
		{
			if (node.Attrs[0, Missing_].IsIdNamed(S.TriviaNewline))
				return node;
			return node.PlusAttrBefore(TriviaNewline);
		}

		#endregion

		#region Id(), Literal() and Triva()

		// Atoms: identifier symbols (including keywords) and literals
		public LNode Id(string name, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdIdNode(GSymbol.Get(name), new SourceRange(_file, startIndex, endIndex-startIndex));
		}
		public LNode Id(Symbol name, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdIdNode(name, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Id(Symbol name, Token t)
		{
			return new StdIdNode(name, new SourceRange(_file, t.StartIndex, t.Length));
		}
		public LNode Id(Token t)
		{
			return new StdIdNode(t.Value as Symbol ?? GSymbol.Get((t.Value ?? "").ToString()),
				new SourceRange(_file, t.StartIndex, t.Length), t.Style);
		}

		public LiteralNode Literal<V>(V value, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdLiteralNode<SimpleValue<V>>(new SimpleValue<V>(value), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LiteralNode Literal(object value, string typeMarker, int startIndex = -1, int endIndex = -1) => Literal(value, (Symbol)typeMarker, startIndex, endIndex);
		public LiteralNode Literal(object value, Symbol typeMarker, int startIndex = -1, int endIndex = -1)
		{
			return new StdLiteralNode<LiteralFromParser>(
				new LiteralFromParser(value, -1, -1, typeMarker), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}

		/// <summary>Creates a literal from a <see cref="Token"/>. If the token is 
		/// uninterpreted (<see cref="Token.IsUninterpretedLiteral"/>), this method uses 
		/// <see cref="StandardLiteralHandlers.Value"/> to parse the literal's value;
		/// otherwise <see cref="LiteralFromValueOf(Token)"/> is called to create the 
		/// literal based on the <see cref="Token.Value"/>.</summary>
		/// <param name="t">Token to be converted</param>
		public LiteralNode Literal(Token t) => Literal(t, StandardLiteralHandlers.Value);

		/// <summary>Creates a literal from a <see cref="Token"/>. If it's an "ordinary"
		/// token, this function simply calls <see cref="LiteralFromValueOf(Token)"/>.
		/// If it is an "uninterpreted literal" (type marker with text), this function 
		/// uses the parser provided to interpret the literal, or, if <c>parser</c> is null, 
		/// calls <see cref="UninterpretedLiteral(Token)"/> to create the node without 
		/// parsing it.</summary>
		/// <param name="t">Token to be converted</param>
		/// <param name="parser">Used to obtain a value from an uninterpreted literal,
		/// which becomes the <see cref="LNode.Value"/> property.</param>
		public LiteralNode Literal(Token t, ILiteralParser parser)
		{
			if (t.IsUninterpretedLiteral)
			{
				if (parser != null)
				{
					var textValue = t.TextValue(_file.Text);
					var parsed = parser.TryParse(textValue, t.TypeMarker);
					if (parsed.Right.HasValue) {
						var result = UninterpretedLiteral(t);
						var lm = parsed.Right.Value;
						ErrorSink.Write(lm.Severity, result, lm.Format, lm.Args);
						return result;
					} else {
						return t.UninterpretedTokenToLNode(_file, parsed.Left.Value);
					}
				}
				return UninterpretedLiteral(t);
			}
			else
				return LiteralFromValueOf(t);
		}

		/// <summary>Creates a literal whose <see cref="LNode.Value"/> is the same as the value of <c>t</c>.</summary>
		public LiteralNode LiteralFromValueOf(Token t)
		{
			return new StdLiteralNode<SimpleValue<object>>(new SimpleValue<object>(t.Value), new SourceRange(_file, t.StartIndex, t.Length), t.Style);
		}
		
		/// <summary>This method produces a literal by assuming that the provided token 
		/// is an uninterpreted literal (see <see cref="Token.IsUninterpretedLiteral"/>)
		/// in the current file and that it does not need to be parsed. Therefore, 
		/// <c>t.Value</c> becomes the <see cref="LNode.TypeMarker"/>, 
		/// <c>t.TextValue(_file.Text)</c> becomes the <see cref="LNode.TextValue"/>, 
		/// and the <see cref="LNode.Value"/> will be a boxed copy of TextValue.</summary>
		public LiteralNode UninterpretedLiteral(Token t)
		{
			Debug.Assert(t.IsUninterpretedLiteral);
			var litVal = new UninterpretedLiteral(t.TextValue(_file.Text), t.TypeMarker);
			return new StdLiteralNode<UninterpretedLiteral>(litVal, SourceRange.New(_file, t));
		}

		/// <summary>Creates a trivia node named <c>"%" + suffix</c> with the 
		/// specified Value attached.</summary>
		/// <remarks>This method only adds the prefix <c>%</c> if it is not 
		/// already present in the 'suffix' argument.</remarks>
		public LNode Trivia(string suffix, object value)
		{
			string name = suffix.StartsWith("%") ? suffix : "%" + suffix;
			return LNode.Trivia(GSymbol.Get(name), value, new SourceRange(_file));
		}
		/// <summary>Creates a trivia node with the specified Value attached.</summary>
		/// <seealso cref="LNode.Trivia(Symbol, object, LNode)"/>
		public LNode Trivia(Symbol name, object value, int startIndex = -1, int endIndex = -1)
		{
			return LNode.Trivia(name, value, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}

		#endregion

		#region Call with LNode target

		public LNode Call(LNode target, IEnumerable<LNode> args, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdComplexCallNode(target, new LNodeList(args), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(LNode target, LNodeList args, int startIndex = -1, int endIndex = -1, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdComplexCallNode(target, args, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), style);
		}
		public LNode Call(LNode target, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdComplexCallNode(target, LNodeList.Empty, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(LNode target, LNode _1, int startIndex = -1, int endIndex = -1, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdComplexCallNode(target, new LNodeList(_1), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), style);
		}
		public LNode Call(LNode target, LNode _1, LNode _2, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdComplexCallNode(target, new LNodeList(_1, _2), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(LNode target, LNode _1, LNode _2, LNode _3, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdComplexCallNode(target, new LNodeList(_1, _2).Add(_3), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(LNode target, LNode _1, LNode _2, LNode _3, LNode _4, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdComplexCallNode(target, new LNodeList(_1, _2).Add(_3).Add(_4), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(LNode target, params LNode[] list)
		{
			return new StdComplexCallNode(target, new LNodeList(list), new SourceRange(_file));
		}
		public LNode Call(LNode target, LNode[] list, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdComplexCallNode(target, new LNodeList(list), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}

		#endregion

		#region Call with Symbol target (and optional target range)

		public LNode Call(Symbol target, IEnumerable<LNode> args, int startIndex = -1, int endIndex = -1)
		{
			return Call(target, new LNodeList(args), startIndex, endIndex);
		}
		public LNode Call(Symbol target, LNodeList args, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, args, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(Symbol target, LNodeList args, int startIndex, int endIndex, int targetStart, int targetEnd, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, args, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), targetStart, targetEnd, style);
		}
		public LNode Call(Symbol target, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, LNodeList.Empty, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(Symbol target, LNode _1, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, new LNodeList(_1), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(Symbol target, LNode _1, LNode _2, int startIndex = -1, int endIndex = -1, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, new LNodeList(_1, _2), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), style);
		}
		public LNode Call(Symbol target, LNode _1, LNode _2, LNode _3, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, new LNodeList(_1, _2).Add(_3), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(Symbol target, LNode _1, LNode _2, LNode _3, LNode _4, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, new LNodeList(_1, _2).Add(_3).Add(_4), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Call(Symbol target, int startIndex, int endIndex, int targetStart, int targetEnd, NodeStyle style = NodeStyle.Default)
		{
			Debug.Assert(targetEnd >= targetStart && targetStart >= startIndex);
			return new StdSimpleCallNode(target, LNodeList.Empty, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), targetStart, targetEnd, style);
		}
		public LNode Call(Symbol target, LNode _1, int startIndex, int endIndex, int targetStart, int targetEnd, NodeStyle style = NodeStyle.Default)
		{
			Debug.Assert(targetEnd >= targetStart && targetStart >= startIndex);
			return new StdSimpleCallNode(target, new LNodeList(_1), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), targetStart, targetEnd, style);
		}
		public LNode Call(Symbol target, LNode _1, LNode _2, int startIndex, int endIndex, int targetStart, int targetEnd, NodeStyle style = NodeStyle.Default)
		{
			Debug.Assert(targetEnd >= targetStart && targetStart >= startIndex);
			return new StdSimpleCallNode(target, new LNodeList(_1, _2), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), targetStart, targetEnd, style);
		}
		public LNode Call(Symbol target, params LNode[] args)
		{
			return new StdSimpleCallNode(target, new LNodeList(args), new SourceRange(_file));
		}
		public LNode Call(Symbol target, LNode[] args, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, new LNodeList(args), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}

		#endregion

		#region Call with string target (string is simply converted to a Symbol)

		public LNode Call(string target, IEnumerable<LNode> args, int startIndex = -1, int endIndex = -1)
		{
			return Call(GSymbol.Get(target), args, startIndex, endIndex);
		}
		public LNode Call(string target, LNodeList args, int startIndex = -1, int endIndex = -1)
		{
			return Call(GSymbol.Get(target), args, startIndex, endIndex);
		}
		public LNode Call(string target, int startIndex = -1, int endIndex = -1)
		{
			return Call(GSymbol.Get(target), startIndex, endIndex);
		}
		public LNode Call(string target, LNode _1, int startIndex = -1, int endIndex = -1)
		{
			return Call(GSymbol.Get(target), _1, startIndex, endIndex);
		}
		public LNode Call(string target, LNode _1, LNode _2, int startIndex = -1, int endIndex = -1)
		{
			return Call(GSymbol.Get(target), _1, _2, startIndex, endIndex);
		}
		public LNode Call(string target, LNode _1, LNode _2, LNode _3, int startIndex = -1, int endIndex = -1)
		{
			return Call(GSymbol.Get(target), _1, _2, _3, startIndex, endIndex);
		}
		public LNode Call(string target, LNode _1, LNode _2, LNode _3, LNode _4, int startIndex = -1, int endIndex = -1)
		{
			return Call(GSymbol.Get(target), _1, _2, _3, _4, startIndex, endIndex);
		}
		public LNode Call(string target, params LNode[] args)
		{
			return Call(GSymbol.Get(target), args);
		}
		public LNode Call(string target, LNode[] args, int startIndex = -1, int endIndex = -1)
		{
			return Call(GSymbol.Get(target), args, startIndex, endIndex);
		}

		#endregion

		#region Call with Token target (uses Token.Value as Symbol and Token range as target range)

		public LNode Call(Token target, IEnumerable<LNode> args, int startIndex = -1, int endIndex = -1, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, new LNodeList(args), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), style);
		}
		public LNode Call(Token target, LNodeList args, int startIndex = -1, int endIndex = -1, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, args, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), style);
		}
		public LNode Call(Token target, int startIndex = -1, int endIndex = -1, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, LNodeList.Empty, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), style);
		}
		public LNode Call(Token target, LNode _1, int startIndex = -1, int endIndex = -1, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, new LNodeList(_1), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), style);
		}
		public LNode Call(Token target, LNode _1, LNode _2, int startIndex = -1, int endIndex = -1, NodeStyle style = NodeStyle.Default)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(target, new LNodeList(_1, _2), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), style);
		}

		#endregion

		#region Calls with automatic ranges: CallPrefix, CallPrefixOp, CallInfixOp, CallSuffixOp, CallBrackets
		// Assumptions:
		// 1. the left-hand side of each expression has a valid range but the right-hand side might be Missing.
		// 2. If a token represents an operator, Value is its Symbol

		public LNode CallPrefixOp(Token target, LNode rhs, Symbol opName = null, NodeStyle style = NodeStyle.Operator) =>
			CallPrefixOp(opName ?? (Symbol)target.Value, target, rhs, style);
		public LNode CallPrefixOp(Token target, LNodeList args, Symbol opName = null, NodeStyle style = NodeStyle.Default) =>
			CallPrefixOp(opName ?? (Symbol)target.Value, target, args, style);
		public LNode CallPrefixOp(Symbol target, IndexRange targetRange, LNode rhs, NodeStyle style = NodeStyle.Operator) =>
			Call(target, rhs, targetRange.StartIndex, Max(rhs.Range.EndIndex, targetRange.EndIndex), targetRange.StartIndex, targetRange.EndIndex, style);
		public LNode CallPrefixOp(Symbol target, IndexRange targetRange, LNodeList args, NodeStyle style = NodeStyle.Default) =>
			Call(target, args, targetRange.StartIndex, Max(args[args.Count - 1, Missing].Range.EndIndex, targetRange.EndIndex), targetRange.StartIndex, targetRange.EndIndex, style);
		public LNode CallPrefixOp(LNode target, LNode rhs, NodeStyle style = NodeStyle.Operator) =>
			Call(target, rhs, target.Range.StartIndex, Max(rhs.Range.EndIndex, target.Range.EndIndex), style);
		public LNode CallPrefixOp(LNode target, LNodeList args, NodeStyle style = NodeStyle.Default) =>
			Call(target, args, target.Range.StartIndex, Max(args[args.Count - 1, Missing].Range.EndIndex, target.Range.EndIndex), style);

		public LNode CallPrefix(Token target, LNode rhs, IndexRange closingBracket, Symbol opName = null, NodeStyle style = NodeStyle.Default) =>
			CallPrefix(opName ?? (Symbol)target.Value, target, LNode.List(rhs), closingBracket, style);
		public LNode CallPrefix(Token target, LNodeList args, IndexRange closingBracket, Symbol opName = null, NodeStyle style = NodeStyle.Default) =>
			CallPrefix(opName ?? (Symbol)target.Value, target, args, closingBracket, style);
		public LNode CallPrefix(LNode target, LNodeList args, IndexRange closingBracket, NodeStyle style = NodeStyle.Default) =>
			Call(target, args, target.Range.StartIndex, Max(closingBracket.EndIndex, (args.IsEmpty ? target : args.Last).Range.EndIndex), style);
		public LNode CallPrefix(Symbol target, IndexRange targetRange, LNodeList args, IndexRange closingBracket, NodeStyle style = NodeStyle.Operator) =>
			Call(target, args, targetRange.StartIndex, Max(closingBracket.EndIndex, args.IsEmpty ? targetRange.EndIndex : args.Last.Range.EndIndex), targetRange.StartIndex, targetRange.EndIndex, style);

		public LNode CallInfixOp(LNode lhs, Token target, LNode rhs, Symbol opName = null, NodeStyle style = NodeStyle.Operator) =>
			CallInfixOp(lhs, opName ?? (Symbol)target.Value, target, rhs, style);
		public LNode CallInfixOp(LNode lhs, Symbol target, IndexRange targetRange, LNode rhs, NodeStyle style = NodeStyle.Operator) =>
			Call(target, lhs, rhs, lhs.Range.StartIndex, Max(rhs.Range.EndIndex, targetRange.EndIndex), targetRange.StartIndex, targetRange.EndIndex, style);

		public LNode CallSuffixOp(LNode lhs, Token target, Symbol opName = null, NodeStyle style = NodeStyle.Operator) =>
			CallSuffixOp(lhs, opName ?? (Symbol)target.Value, target, style);
		public LNode CallSuffixOp(LNode lhs, Symbol target, IndexRange targetRange, NodeStyle style = NodeStyle.Operator) =>
			Call(target, lhs, lhs.Range.StartIndex, targetRange.EndIndex, targetRange.StartIndex, targetRange.EndIndex, style);

		public LNode CallInfixOp(LNode lhs, Token target, LNode middle, LNode rhs, Symbol opName = null, NodeStyle style = NodeStyle.Operator) =>
			CallInfixOp(lhs, opName ?? (Symbol)target.Value, target, middle, rhs, style);
		public LNode CallInfixOp(LNode lhs, Symbol target, IndexRange targetRange, LNode middle, LNode rhs, NodeStyle style = NodeStyle.Operator) =>
			Call(target, LNode.List(lhs, middle, rhs), lhs.Range.StartIndex, Max(rhs.Range.EndIndex, middle.Range.EndIndex), targetRange.StartIndex, targetRange.EndIndex, style);
		
		public LNode CallBrackets(Symbol opName, Token lhs, LNodeList args, Token rhs, NodeStyle style = NodeStyle.Default) =>
			Call(opName, args, lhs.StartIndex, Max(rhs.EndIndex, args.IsEmpty ? lhs.EndIndex : args[0].Range.EndIndex), lhs.StartIndex, lhs.EndIndex, style);

		#endregion

		#region Dot()

		public LNode Dot(Symbol prefix, Symbol symbol)
		{
			return new StdSimpleCallNode(S.Dot, new LNodeList(Id(prefix), Id(symbol)), new SourceRange(_file));
		}
		public LNode Dot(params string[] symbols)
		{
			return Dot(symbols.SelectArray(s => Id(GSymbol.Get(s))));
		}
		public LNode Dot(params Symbol[] symbols)
		{
			return Dot(symbols.SelectArray(s => Id(s)));
		}
		public LNode Dot(params LNode[] parts)
		{
			int start = parts[0].Range.StartIndex;
			if (parts.Length == 1) {
				start = System.Math.Max(start, 0);
				return Call(S.Dot, parts[0], start - 1, parts[0].Range.EndIndex, start - 1, start);
			}
			var expr = Call(S.Dot, parts[0], parts[1], start, parts[1].Range.EndIndex);
			for (int i = 2; i < parts.Length; i++)
				expr = Call(S.Dot, expr, parts[i], start, parts[i].Range.EndIndex);
			return expr;
		}
		public LNode Dot(LNode prefix, Symbol symbol, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(S.Dot, new LNodeList(prefix, Id(symbol)), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Dot(LNode prefix, LNode symbol, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(S.Dot, new LNodeList(prefix, symbol), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Dot(LNode prefix, LNode symbol, int startIndex, int endIndex, int dotStart, int dotEnd, NodeStyle style = NodeStyle.Default)
		{
			return new StdSimpleCallNode(S.Dot, new LNodeList(prefix, symbol), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), dotStart, dotEnd, style);
		}
		public LNode Dot(LNode lhs, IndexRange dotRange, LNode rhs, NodeStyle style = NodeStyle.Operator)
		{
			int startIndex = lhs.Range.StartIndex, endIndex = Max(rhs.Range.EndIndex, dotRange.EndIndex);
			return new StdSimpleCallNode(S.Dot, new LNodeList(lhs, rhs), new SourceRange(_file, startIndex, endIndex - startIndex), dotRange.StartIndex, dotRange.EndIndex, style);
		}

		#endregion

		#region Of() (for creating generics like List<T>)

		public LNode Of(params Symbol[] list)
		{
			return new StdSimpleCallNode(S.Of, new LNodeList(list.SelectArray(sym => Id(sym))), new SourceRange(_file));
		}
		public LNode Of(params LNode[] list)
		{
			return new StdSimpleCallNode(S.Of, new LNodeList(list), new SourceRange(_file));
		}
		public LNode Of(LNode stem, LNode T1, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return Call(S.Of, stem, T1, startIndex, endIndex);
		}
		public LNode Of(Symbol stem, LNode T1, int startIndex = -1, int endIndex = -1)
		{
			return Of(Id(stem), T1, startIndex, endIndex);
		}
		public LNode Of(LNode stem, IEnumerable<LNode> typeParams, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return Call(S.Of, stem, startIndex, endIndex).PlusArgs(typeParams);
		}
		public LNode Of(Symbol stem, IEnumerable<LNode> typeParams, int startIndex = -1, int endIndex = -1)
		{
			return Of(Id(stem), typeParams, startIndex, endIndex);
		}

		#endregion

		#region NamedArg() (for named arguments)

		public LNode NamedArg(string name, LNode arg, int startIndex = -1, int endIndex = -1)
			=> Call(S.NamedArg, LNode.Id(name), arg, startIndex, endIndex);
		public LNode NamedArg(Symbol name, LNode arg, int startIndex = -1, int endIndex = -1)
			=> Call(S.NamedArg, LNode.Id(name), arg, startIndex, endIndex);
		public LNode NamedArg(LNode name, LNode arg)
			=> Call(S.NamedArg, name, arg, name.Range.StartIndex, Max(arg.Range.EndIndex, name.Range.EndIndex));

		#endregion

		#region Braces()

		public LNode Braces(params LNode[] contents)
		{
			return Braces(new LNodeList(contents));
		}
		public LNode Braces(LNodeList contents, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			if (endIndex > startIndex)
				return new StdSimpleCallNode(S.Braces, contents, 
					new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), 
					startIndex, startIndex + (endIndex > startIndex + 1 ? 1 : 0));
			else
				return new StdSimpleCallNode(S.Braces, contents, 
					new SourceRange(_file, startIndex, 0));
		}
		public LNode Braces(LNode[] contents, int startIndex = -1, int endIndex = -1)
		{
			return Braces(new LNodeList(contents), startIndex, endIndex);
		}
		public LNode Braces(IEnumerable<LNode> contents, int startIndex = -1, int endIndex = -1)
		{
			return Braces(new LNodeList(contents), startIndex, endIndex);
		}
		public LNode Braces(Token openBrace, LNodeList contents, Token closeBrace, NodeStyle style = NodeStyle.StatementBlock)
		{
			int endIndex = Max(closeBrace.EndIndex, contents.IsEmpty ? openBrace.EndIndex : contents.Last.Range.EndIndex);
			return Call(S.Braces, contents, openBrace.StartIndex, endIndex, openBrace.StartIndex, openBrace.EndIndex, style);
		}

		#endregion

		#region AltList(), Splice() and Tuple()

		[Obsolete("This method has been renamed to AltList")]
		public LNode List() => AltList();
		[Obsolete("This method has been renamed to AltList")]
		public LNode List(params LNode[] contents) => AltList(contents);
		[Obsolete("This method has been renamed to AltList")]
		public LNode List(LNodeList contents, int startIndex = -1, int endIndex = -1) => AltList(contents, startIndex, endIndex);
		[Obsolete("This method has been renamed to AltList")]
		public LNode List(IEnumerable<LNode> contents, int startIndex = -1, int endIndex = -1) => AltList(contents, startIndex, endIndex);

		public LNode AltList()
		{
			if (_emptyList == null) 
				_emptyList = Call(S.AltList);
			return _emptyList;
		}
		public LNode AltList(params LNode[] contents)
		{
			return Call(S.AltList, contents, -1, -1);
		}
		public LNode AltList(LNodeList contents, int startIndex = -1, int endIndex = -1)
		{
			return Call(S.AltList, contents, startIndex, endIndex);
		}
		public LNode AltList(IEnumerable<LNode> contents, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return Call(S.AltList, contents, startIndex, endIndex);
		}

		public LNode Splice()
		{
			if (_emptySplice == null) 
				_emptySplice = Call(S.Splice);
			return _emptySplice;
		}
		public LNode Splice(params LNode[] contents)
		{
			return Call(S.Splice, contents, -1, -1);
		}
		public LNode Splice(LNode[] contents, int startIndex = -1, int endIndex = -1)
		{
			return Call(S.Splice, contents, startIndex, endIndex);
		}
		public LNode Splice(LNodeList contents, int startIndex = -1, int endIndex = -1)
		{
			return Call(S.Splice, contents, startIndex, endIndex);
		}
		public LNode Splice(IEnumerable<LNode> contents, int startIndex = -1, int endIndex = -1)
		{
			return Call(S.Splice, contents, startIndex, endIndex);
		}

		public LNode Tuple()
		{
			if (_emptyTuple == null) 
				_emptyTuple = Call(S.Tuple);
			return _emptyTuple;
		}
		public LNode Tuple(params LNode[] contents)
		{
			return Tuple(contents, -1);
		}
		public LNode Tuple(LNode[] contents, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(S.Tuple, new LNodeList(contents), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Tuple(LNodeList contents, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return new StdSimpleCallNode(S.Tuple, contents, new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)));
		}
		public LNode Tuple(IEnumerable<LNode> contents, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return Call(S.Tuple, contents, startIndex, endIndex);
		}

		#endregion

		#region Function, property and variable definitions

		public LNode Fn(LNode retType, Symbol name, LNode argList, LNode body = null, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return Fn(retType, Id(name), argList, body, startIndex, endIndex);
		}
		public LNode Fn(LNode retType, LNode name, LNode argList, LNode body = null, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			CheckParam.Arg("argList", argList.Name == S.AltList || argList.Name == S.Missing);
			LNode[] list = body == null 
				? new[] { retType, name, argList }
				: new[] { retType, name, argList, body };
			return new StdSimpleCallNode(S.Fn, new LNodeList(list), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), startIndex, startIndex);
		}
		public LNode Property(LNode type, LNode name, LNode body = null, int startIndex = -1, int endIndex = -1)
		{
			return Property(type, name, Missing_, body, null, startIndex, endIndex);
		}
		public LNode Property(LNode type, LNode name, LNode argList, LNode body, LNode initializer = null, int startIndex = -1, int endIndex = -1)
		{
			argList = argList ?? Missing_;
			CheckParam.Arg("body with initializer", initializer == null || (body != null && body.Calls(S.Braces)));
			if (endIndex < startIndex) endIndex = startIndex;
			LNode[] list = body == null
				? new[] { type, name, argList, }
				: initializer == null
				? new[] { type, name, argList, body }
				: new[] { type, name, argList, body, initializer };
			return new StdSimpleCallNode(S.Property, new LNodeList(list), new SourceRange(_file, startIndex, Max(endIndex - startIndex, 0)), startIndex, startIndex);
		}
		
		public LNode Var(LNode type, string name, LNode initValue = null, int startIndex = -1, int endIndex = -1)
		{
			return Var(type, GSymbol.Get(name), initValue, startIndex, endIndex);
		}
		public LNode Var(LNode type, Symbol name, LNode initValue = null, int startIndex = -1, int endIndex = -1)
		{
			return Var(type, Id(name), initValue, startIndex, endIndex);
		}
		public LNode Var(LNode type, LNode name, LNode initValue = null, int startIndex = -1, int endIndex = -1)
		{
			type = type ?? Missing;
			if (initValue != null)
				return Call(S.Var, type, Call(S.Assign, name, initValue), startIndex, endIndex);
			else
				return Call(S.Var, type, name, startIndex, endIndex);
		}
		public LNode Var(LNode type, LNode name)
		{
			return Call(S.Var, type ?? Missing, name);
		}
		public LNode Vars(LNode type, params Symbol[] names)
		{
			type = type ?? Missing;
			var list = new List<LNode>(names.Length + 1) { type };
			list.AddRange(names.Select(n => Id(n)));
			return Call(S.Var, list.ToArray());
		}
		public LNode Vars(LNode type, params LNode[] namesWithValues)
		{
			type = type ?? Missing;
			var list = new WList<LNode>() { type };
			list.AddRange(namesWithValues);
			return Call(S.Var, list.ToVList());
		}

		#endregion

		#region Other stuff

		public LNode InParens(LNode inner)
		{
			return LNodeExt.InParens(inner);
		}
		public LNode InParens(LNode inner, int startIndex, int endIndex)
		{
			return LNodeExt.InParens(inner, File, startIndex, Max(endIndex, startIndex));
		}
		public LNode InParens(IndexRange leftParen, LNode inner, IndexRange rightParen)
		{
			return LNodeExt.InParens(inner, File, leftParen.StartIndex, Max(rightParen.EndIndex, inner.Range.EndIndex));
		}

		public LNode Result(LNode expr)
		{
			return Call(S.Result, expr, expr.Range.StartIndex, expr.Range.EndIndex);
		}

		public LNode Attr(LNode attr, LNode node)
		{
			return node.PlusAttrBefore(attr);
		}
		public LNode Attr(params LNode[] attrsAndNode)
		{
			var node = attrsAndNode[attrsAndNode.Length - 1];
			var newAttrs = node.Attrs.InsertRange(0, attrsAndNode.Slice(0, attrsAndNode.Length-1));
			return node.WithAttrs(newAttrs);
		}

		public LNode Assign(Symbol lhs, LNode rhs, int startIndex = -1, int endIndex = -1)
		{
			return Assign(Id(lhs), rhs, startIndex, endIndex);
		}
		public LNode Assign(LNode lhs, LNode rhs, int startIndex = -1, int endIndex = -1)
		{
			if (endIndex < startIndex) endIndex = startIndex;
			return Call(S.Assign, new LNodeList(lhs, rhs), startIndex, endIndex);
		}

		#endregion
	}
}
