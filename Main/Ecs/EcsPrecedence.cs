using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Loyc;
using Loyc.Syntax;

namespace Loyc.Ecs
{
	/// <summary>Contains <see cref="Precedence"/> objects that represent the 
	/// precedence rules of EC#.</summary>
	/// <remarks>
	/// This precedence table uses the immiscibility concept represented by the
	/// <see cref="Precedence.Lo"/> and <see cref="Precedence.Hi"/> properties.
	/// When printing an expression, we avoid emitting <c>x | y == z</c> because 
	/// the ranges of == and | overlap. Instead <see cref="EcsNodePrinter"/> prints 
	/// <c>@`'|`(x, y == z)</c>. Admittedly this is rather ugly, but you can enable
	/// the <see cref="ILNodePrinterOptions.AllowChangeParentheses"/> option, which allows 
	/// parenthesis to be added so that a Loyc tree with the structure 
	/// <c>@`'|`(x, y == z)</c> is emitted as <c>x | (y == z)</c>, even though the 
	/// latter is a slightly different tree.
	/// <para/>
	/// Most of the operators use a range of two adjacent numbers, e.g. 10..11. 
	/// This represents a couple of ideas for future use in a compiler that allows
	/// you to define new operators; one idea is, you could give new operators the
	/// "same" precedence as existing operators, but make them immiscible with 
	/// those operators... yet still make them miscible with another new operator.
	/// For instance, suppose you define two new operators `glob` and `fup` with
	/// PrecedenceRange 41..41 and 40..40 respectively. Then neither can be mixed
	/// with + and -, but they can be mixed with each other and `fup` has higher
	/// precedence. Maybe this is not very useful, but hey, why not? If simply
	/// incrementing a number opens up new extensibility features, I'm happy to
	/// do it. (I could have used a non-numeric partial ordering system to do
	/// the same thing, but it would have been more complex, and of questionable
	/// value.)
	/// </remarks>
	/// <seealso cref="Precedence"/>
	public static class EcsPrecedence
	{
		public static readonly Precedence Substitute = new Precedence(106, 105, 105, 106); // $x  .x
		public static readonly Precedence Of         = new Precedence(102);            //!< List<T>
		public static readonly Precedence Primary    = new Precedence(100);            //!< x.y x::y x=:y x->y f(x) x(->y) a[x] x++ x-- typeof() checked() unchecked() new
		/// <summary>Officially the `?.` operator (and its weird ternary cousin `?[...]`) does 
		/// not have its own precedence level, but we can infer it exists: plain C# returns 
		/// null for the expression <c>((string)null)?.Trim().Length</c>, and throws an exception
		/// for <c>(((string)null)?.Trim()).Length</c>. Its cousin `?[...]` behaves similarly: 
		/// <c>((string)null)?[5].ToString()</c> is null, while <c>(((string)null)?[5]).ToString()</c> 
		/// is "" (because <c>((char?)null).ToString()</c> returns ""</c>).</summary>
		public static readonly Precedence NullDot    = new Precedence(99);             
		public static readonly Precedence Prefix     = new Precedence(91, 90, 90, 91); //!<  +  -  !  ~  ++x  --x  (T)x
		public static readonly Precedence Power      = new Precedence(85);             //!<  ** (tentatively left-associative)
		public static readonly Precedence Range      = new Precedence(80);             //!<  ..
		public static readonly Precedence Forward    = new Precedence(78);             //!<  ==> x
		public static readonly Precedence Switch     = new Precedence(75);             //!<  with, switch
		public static readonly Precedence Multiply   = new Precedence(70);             //!<  *, /, %
		public static readonly Precedence Add        = new Precedence(60);             //!<  +, -, ~
		public static readonly Precedence Shift      = new Precedence(56, 56, 56, 70); //!<  >> << (for printing purposes, immiscible with * / + -)
		public static readonly Precedence Backtick   = new Precedence(46, 72, 45, 73); //!<  `custom operator` (immiscible with * / + - << >> ..)
		public static readonly Precedence Compare3Way= new Precedence(42);             //!<  <=>
		public static readonly Precedence Compare    = new Precedence(40);             //!<  < > <= >=
		public static readonly Precedence Is         = new Precedence(40, 42, 40, 40); //!<  is
		public static readonly Precedence AsUsing    = new Precedence(40, 99, 40, 40); //!<  as using (casts)
		public static readonly new Precedence Equals = new Precedence(38);             //!<  == != in
		public static readonly Precedence AndBits    = new Precedence(32, 32, 32, 45); //!<  &   (^ and | should not be mixed with Compare/Equals 
		public static readonly Precedence XorBits    = new Precedence(30, 30, 32, 45); //!<  ^    either, but the low-high system cannot express this
		public static readonly Precedence OrBits     = new Precedence(28, 28, 32, 45); //!<  |    while allowing & ^ | to be mixed with each other.)
		public static readonly Precedence And        = new Precedence(22);             //!<  &&
		public static readonly Precedence Or         = new Precedence(20);             //!<  || ^^
		public static readonly Precedence OrIfNull   = new Precedence(17);             //!<  ??
		public static readonly Precedence PipeArrow  = new Precedence(15);             //!<  |>  ?|>  |=>  ?|=>
		public static readonly Precedence PatternNot = new Precedence(13);             //!<  not (right-hand side of is/switch only)
		public static readonly Precedence PatternAnd = new Precedence(12);             //!<  and (right-hand side of is/switch only)
		public static readonly Precedence PatternOr  = new Precedence(11);             //!<  or  (right-hand side of is/switch only)
		public static readonly Precedence IfElse     = new Precedence(11, 10, 10, 11); //!<  x ? y : z
		public static readonly Precedence WhenWhere  = new Precedence(5);              //!<  when, where
		public static readonly Precedence Assign     = new Precedence(26,  0,  0, 1);  //!<  =  *=  /=  %=  +=  -=  <<=  >>=  &=  ^=  |= ??= ~=
		public static readonly Precedence Lambda     = new Precedence(85, -1, -2, -1); //!<  =>
	}
}
