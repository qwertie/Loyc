using System;
using System.Collections.Generic;
using System.Linq;
using Loyc.MiniTest;
using Loyc.Syntax;
using S = Loyc.Syntax.CodeSymbols;

namespace Loyc.Ecs.Tests
{
	// These are generally tests that are much harder for the printer than for the 
	// parser. There are more printer-focused tests sprinkled throughout the other 
	// test files, too. Although these tests are meant to ensure the printer works
	// right, they are tested with the parser too, just because we can.
	partial class EcsPrinterAndParserTests
	{
		[Test]
		public void PrinterOptionsTest()
		{
			// MixImmiscibleOperators is tested elsewhere
			Option(Mode.PrintBothParseFirst, @"b(->Foo)(x);", @"((Foo) b)(x);", F.Call(F.Call(S.Cast, b, Foo), x), p => p.SetPlainCSharpMode());
			Option(Mode.Both,       @"b(x)(->Foo);", @"(Foo) b(x);", Alternate(F.Call(S.Cast, F.Call(b, x), Foo)), p => p.PreferPlainCSharp = true);
			Option(Mode.Both,       @"yield return x;", @"yield return x;", Attr(WordAttr("yield"), F.Call(S.Return, x)), p => p.SetPlainCSharpMode());
			
			Action<EcsPrinterOptions> parens = p => p.AllowChangeParentheses = true;
			Option(Mode.PrintBothParseFirst, @"@`'+`(a, b) / c;", @"(a + b) / c;", F.Call(S.Div, F.Call(S.Add, a, b), c), parens);
			Option(Mode.PrintBothParseFirst, @"@`'-`(a)++;",      @"(-a)++;",      F.Call(S.PostInc, F.Call(S._Negate, a)), parens);
		}

		[Test]
		public void DropNonDeclarationAttributesTest()
		{
			// Put attributes in various locations and watch them disappear....
			// New rule (2020/02): don't drop normal expression-scoped attributes except inside
			//   methods. The reason is so that `public Foo(int? x) => _x = x;` is being parsed
			//   as a "lambda expression" rather than as a constructor and the old behavior of
			//   dropping attributes in this case was problematic. With this new rule, we need
			//   to test both the method and non-method contexts.
			Action<EcsPrinterOptions> dropAttrs = p => p.DropNonDeclarationAttributes = true;
			ExpectAttrsDroppedWhenAsked(@"#var(static Foo, x);", @"Foo x;", F.Vars(Attr(@static, Foo), x));
			ExpectAttrsDroppedWhenAsked(@"#var(Foo, static x);", @"Foo x;", F.Vars(Foo, Attr(@static, x)));
			ExpectAttrsDroppedWhenAsked(@"#var(Foo<a>, [#foo] b, c = 1);", @"Foo<a> b, c = 1;", F.Vars(F.Of(Foo, a), Attr(fooKW, b), F.Assign(c, one)));
			ExpectAttrsDroppedWhenAsked(@"#var(Foo!(static a), b);", @"Foo<a> b;", F.Vars(F.Of(Foo, Attr(@static, a)), b));
			ExpectAttrsDroppedWhenAsked(@"#var(@'of(static Foo, a), b);", @"Foo<a> b;", F.Vars(F.Of(Attr(@static, Foo), a), b));
			ExpectAttrsDroppedWhenAsked(@"([Foo] a)(x);", @"a(x);", F.Call(Attr(Foo, a), x));

			ExpectAttrsDroppedWhenAsked(@"x[[Foo] a];", @"x[a];", F.Call(S.IndexBracks, x, Attr(Foo, a)));
			ExpectAttrsDroppedWhenAsked(@"a([#foo] x);", @"a(x);", F.Call(a, Attr(fooKW, x)));

			ExpectAttrsDroppedOnlyInMethod("[Foo] a + b;", @"a + b;", Attr(Foo, F.Call(S.Add, a, b)));
			ExpectAttrsDroppedOnlyInMethod(@"public a(x);", @"a(x);", Attr(@public, F.Call(a, x)));
			ExpectAttrsDroppedOnlyInMethod("[Foo] a + b;", @"a + b;", Attr(Foo, F.Call(S.Add, a, b)));
			var stmt = Attr(x, @public, F.Call(S.Lambda, F.Call(Foo, F.Var(F.Int32, x, zero)), F.Assign(a, x)));
			ExpectAttrsDroppedOnlyInMethod("[x] public Foo(int x = 0) => a = x;", @"Foo(int x = 0) => a = x;", stmt);

			ExpectAttrsAreNeverDropped(@"[Foo] public void Foo() { }", Attr(Foo, @public, F.Fn(F.Void, Foo, F.AltList(), F.Braces())));
			ExpectAttrsAreNeverDropped(@"[Foo] public class Foo { }", Attr(Foo, @public, F.Call(S.Class, Foo, F.AltList(), F.Braces())));
			ExpectAttrsAreNeverDropped(@"[Foo] public Foo x;", Attr(Foo, @public, F.Var(Foo, x)));

			ExpectAttrsDroppedWhenAsked(@"@`'suf[]`(static x, a);", @"x[a];", F.Call(S.IndexBracks, Attr(@static, x), a));
			ExpectAttrsDroppedWhenAsked(@"@`'+`([Foo] a, 1);", @"a + 1;", F.Call(S.Add, Attr(Foo, a), one));
			ExpectAttrsDroppedWhenAsked(@"@`'+`(a, [Foo] 1);", @"a + 1;", F.Call(S.Add, a, Attr(Foo, one)));
			ExpectAttrsDroppedWhenAsked(@"@`'?`(a, [#foo] b, c);", @"a ? b : c;", F.Call(S.QuestionMark, a, Attr(fooKW, b), c));
			ExpectAttrsDroppedWhenAsked(@"@`'?`(a, b, public c);", @"a ? b : c;", F.Call(S.QuestionMark, a, b, Attr(@public, c)));
			ExpectAttrsDroppedWhenAsked(@"@`'++`([Foo] x);", @"++x;", F.Call(S.PreInc, Attr(Foo, x)));
			ExpectAttrsDroppedWhenAsked(@"@`'suf++`([Foo] x);", @"x++;", F.Call(S.PostInc, Attr(Foo, x)));
			ExpectAttrsDroppedWhenAsked(@"x(->static Foo);", @"(Foo) x;", F.Call(S.Cast, x, Attr(@static, Foo)));

			// There are no actual attributes here, but the printer may try to insert
			// an empty attribute list in order to add parentheses but suppress %inParens 
			// when round-tripping, and the DropNonDeclarationAttributes flag should
			// suppress such empty attribute lists. This particular case is weird, as 
			// the parens disappear entirely, maybe higher-level code disagrees with 
			// lower-level code about circumstances that need parentheses, but the matter 
			// seems too minor to worry about...
			ExpectAttrsDroppedWhenAsked(@"a.([] -b);", @"a.-b;", F.Dot(a, F.Call(S._Negate, b)));
		}
		static Action<EcsPrinterOptions> _dropAttrsOption = p => p.DropNonDeclarationAttributes = true;
		void ExpectAttrsDroppedWhenAsked(string withAttrs, string withoutAttrs, LNode stmt)
		{
			Option(Mode.PrintBothParseFirst, withAttrs, withoutAttrs, stmt, _dropAttrsOption);
		}
		void ExpectAttrsDroppedOnlyInMethod(string withAttrs, string withoutAttrs, LNode stmt)
		{
			Stmt(withAttrs, stmt, _dropAttrsOption);
			LNode stmtInFooMethod = F.Fn(F.Void, Foo, F.AltList(), F.Braces(stmt));
			string expectedTextInMethod = "void Foo() {\n  " + withoutAttrs + "\n}";
			Stmt(expectedTextInMethod, stmtInFooMethod, _dropAttrsOption, Mode.PrinterTest);
		}
		void ExpectAttrsAreNeverDropped(string withAttrs, LNode stmt)
		{
			// Check that the output stays the same inside a method
			ExpectAttrsDroppedOnlyInMethod(withAttrs, withAttrs, stmt);
		}

		[Test]
		public void PrintWrongArityOperators()
		{
			Expr("@`'suf[]`()",      F.Call(S.IndexBracks));
			Expr("@`'suf++`(a, b)",  F.Call(S.PostInc, a, b));
			Expr("@`'suf--`()",      F.Call(S.PostDec));
			Expr("@`'.`()", F.Call(S.Dot));
			Expr("@`'*`()", F.Call(S.Mul));
			Stmt("#break;",          _(S.Break));
			Stmt("#continue;",       _(S.Continue));
			Stmt("#return;",         _(S.Return));
			Stmt("#throw;",          _(S.Throw));
		}

		public void PrinterRevertsToPrefixNotation()
		{
			LNode EventHandler = _("EventHandler"), stmt;
			// A funky syntax tree causes the printer to revert to prefix notation
			stmt = F.Call(S.Event, F.Call(S.Add, a, b), _("Click"));
			Stmt("#event(a + b, Click);", stmt);
			stmt = F.Call(S.Event, EventHandler, F.Call(S.Add, a, b));
			Expr("#event(EventHandler, a + b)", stmt);
			stmt = F.Call(S.Event, EventHandler, F.Call(S.Add, a, b), F.Braces());
			Expr("#event(EventHandler, a + b, { })", stmt);
			stmt = F.Call(S.Event, EventHandler, F.AltList(a, b), F.Braces());
			Stmt("event EventHandler a, b { }", stmt, Mode.ExpectAndDropParserError);
		}

		public void PrinterNewlineTests()
		{
			LNode code;
			Stmt("struct Foo { }", F.Call(S.Struct, Foo, F.AltList(), F.Braces()), 
				p => p.NewlineOptions |= NewlineOpt.BeforeSpaceDefBrace, Mode.PrinterTest);
			Stmt("void Foo()\n{ }", F.Fn(F.Void, Foo, F.AltList(), F.Braces()), 
				p => p.NewlineOptions |= NewlineOpt.BeforeMethodBrace, Mode.PrinterTest);
			Stmt("int Foo\n{\n  get;\n}", F.Fn(F.Void, Foo, F.AltList(), F.Braces(get)), 
				p => p.NewlineOptions |= NewlineOpt.BeforePropBrace, Mode.PrinterTest);
			code = F.Fn(F.Void, Foo, F.AltList(), F.Braces(F.Call(set, F.Braces())));
			Stmt("int Foo {\n  set\n  { }\n}", code, p => p.NewlineOptions |= NewlineOpt.BeforeGetSetBrace, Mode.PrinterTest);
			code = F.Call(S.Event, F.Id("EventHandler"), F.AltList(a, b), F.Braces());
			Stmt("event EventHandler a, b\n{\n}", code, p => p.NewlineOptions |= NewlineOpt.BeforePropBrace, Mode.PrinterTest);
		}

		[Test]
		public void PrinterCastComplications()
		{
			Expr(@"(a using Foo)(x)",F.Call(F.InParens(F.Call(S.UsingCast, a, Foo)), x));
			Expr(@"a(using Foo)(x)", F.Call(F.Call(S.UsingCast, a, Foo), x));
			Expr(@"(a) b(x)",        F.Call(S.Cast, F.Call(b, x), a));
			Expr(@"b(->a)(x)",       F.Call(F.Call(S.Cast, b, a), x));
			Expr(@"a(as Foo).b",     F.Dot(F.Call(S.As, a, Foo), b));
			Expr(@"$(a using Foo)",  F.Call(S.Substitute, F.Call(S.UsingCast, a, Foo)));
			Expr(@"$(a(using Foo))", F.Call(S.Substitute, Alternate(F.Call(S.UsingCast, a, Foo))));
		}

		[Test]
		public void PrintSpecialCSharpChallenges()
		{
			// Cases that are difficult to handle due to ambiguities inherited from C#
			var neg_a = F.Call(S._Negate, a);
			Expr("(Foo) - a",         F.Call(S.Sub, F.InParens(Foo), a));
			Expr("(Foo) (-a)",        F.Call(S.Cast, F.InParens(neg_a), Foo));
			Expr("(Foo) @`'-`(a)",    F.Call(S.Cast, neg_a, Foo));
			Expr("(Foo) @`'+`(a)",    F.Call(S.Cast, F.Call(S._UnaryPlus, a), Foo));
			var Foo_a = F.Of(Foo, a); 
			Expr("(Foo<a>) (-a)",     F.Call(S.Cast, F.InParens(neg_a), Foo_a));
			Expr("(([] Foo))(-a)",    F.Call(F.InParens(Foo), neg_a));
			// [] certifies "this is not a cast!"; extra parentheses also work
			Option(Mode.PrintBothParseFirst,
				"(([] Foo<a>))(-a);", "((Foo<a>))(-a);",
				F.Call(F.InParens(Foo_a), neg_a), p => p.AllowChangeParentheses = true);
			Expr("(a.b<c>) x",        F.Call(S.Cast, x, F.Dot(a, F.Of(b, c))));
			Expr("x(->a.b!(c > 1))",  F.Call(S.Cast, x, F.Dot(a, F.Of(b, F.Call(S.GT, c, one)))));
			Expr("x(->[Foo] a.b<c>)", F.Call(S.Cast, x, Attr(Foo, F.Dot(a, F.Of(b, c)))));
			// TODO
			//Expr("x(->a * b)",        F.Call(S.Cast, x, F.Call(S.Mul, a, b)));
			Stmt("Foo* a;",           F.Vars(F.Of(_(S._Pointer), Foo), a));
			Stmt("Foo `'*` a = b;",   F.Assign(F.Call(S.Mul, Foo, a), b)); // @*(Foo, a) = b; would also be acceptable
		}

		[Test]
		public void PreprocessorConflicts()
		{
			Stmt("@#error(\"FAIL!\");", F.Call(S.Error, F.Literal("FAIL!")));
			Stmt("@#if(c, Foo());",     AsStyle(NodeStyle.Expression, F.Call(S.If, c, F.Call(Foo))));
			Stmt("@#region(57);",       AsStyle(NodeStyle.Expression, F.Call(GSymbol.Get("#region"), Number(57))));
		}

		[Test]
		public void PrinterParenthesesChallenges()
		{
			Stmt("int x;",             F.Call(S.Var, F.Int32, F.InParens(x)), p => p.AllowChangeParentheses = true, Mode.PrinterTest);
			Stmt("#var(int, (x));",    F.Call(S.Var, F.Int32, F.InParens(x)), p => p.AllowChangeParentheses = false);
			Stmt("int x = (1);",       F.Call(S.Var, F.Int32, F.Call(S.Assign, x, F.InParens(one))), p => p.AllowChangeParentheses = true);
			Stmt("#var(int, (x) = 1);",F.Call(S.Var, F.Int32, F.Call(S.Assign, F.InParens(x), one)), p => p.AllowChangeParentheses = false);
			Stmt("#var(int, (x) = 1);",F.Call(S.Var, F.Int32, F.Call(S.Assign, F.InParens(x), one)), p => p.AllowChangeParentheses = true);
			Option(Mode.PrintBothParseFirst, "#var(int, (x = 1));", "int x = 1;",
				F.Call(S.Var, F.Int32, F.InParens(F.Call(S.Assign, x, one))), p => p.AllowChangeParentheses = true);
			Stmt("#var((int), x);",    F.Call(S.Var, F.InParens(F.Int32), x), p => p.AllowChangeParentheses = false);
			Stmt("#var((int), x);",    F.Call(S.Var, F.InParens(F.Int32), x), p => p.AllowChangeParentheses = true);
			Stmt("(return x);",        F.InParens(F.Call(S.Return, x)));
			Stmt("return x;",          F.InParens(F.Call(S.Return, x)), p => p.AllowChangeParentheses = true, Mode.PrinterTest);
			Stmt("#return(x) = 1;",    F.Call(S.Assign, F.Call(S.Return, x), one));
			Stmt("(return x) = 1;",    F.Call(S.Assign, F.Call(S.Return, x), one), p => p.AllowChangeParentheses = true, Mode.PrinterTest);
			// TODO
			//Expr("x(->(int))",         F.Call(S.Cast, x, F.InParens(F.Int32)), p => p.AllowChangeParenthesis = false);
			//Expr("x(->(int))",         F.Call(S.Cast, x, F.InParens(F.Int32)), p => p.AllowChangeParenthesis = true);
		}

		[Test]
		public void PrinterBreakingAttributes()
		{
			Stmt("@`'.`([Foo] a, b).c;",  F.Dot(Attr(Foo, a), b, c));
			Stmt("@`'.`(a, [Foo] b).c;",  F.Dot(a, Attr(Foo, b), c));
			Stmt("@`'.`(a.b, [Foo] c);",  F.Dot(a, b, Attr(Foo, c)));
			Stmt("@`'.`([Foo] a, b, c);", F.Call(S.Dot, Attr(Foo, a), b, c));
			Stmt("@`'.`(a, b, [Foo] c);", F.Call(S.Dot, a, b, Attr(Foo, c)));
			Expr("@`'+`([Foo] a, b)",     F.Call(S.Add, Attr(Foo, a), b));
			Expr("@`'+`(a, [Foo] b)",     F.Call(S.Add, a, Attr(Foo, b)));
			Expr("@`'suf[]`([Foo] a, b)", F.Call(S.IndexBracks, Attr(Foo, a), b));
			Expr("@`'?`([Foo] c, a, b)",  F.Call(S.QuestionMark, Attr(Foo, c), a, b));
			Expr("@`'?`(c, [Foo] a, b)",  F.Call(S.QuestionMark, c, Attr(Foo, a), b));
			Expr("@`'?`(c, a, [Foo] b)",  F.Call(S.QuestionMark, c, a, Attr(Foo, b)));
		}

		[Test]
		public void PrinterBreakingAttrInHead()
		{
			// Normally we can use prefix notation when children have attributes...
			Stmt("@`'+=`([a] b, c);",    F.Call(S.AddAssign, Attr(a, b), c));
			// But this is no solution if the head of a node has attributes. The only
			// workaround is to add parenthesis.
			Stmt("[a] ([b] c)(x);", Attr(a, F.Call(Attr(b, c), x)), Mode.PrinterTest);
			Stmt("[a] ([b] c())(x);", Attr(a, F.Call(Attr(b, F.Call(c)), x)), Mode.PrinterTest);
		}

		[Test]
		public void Immiscibility()
		{
			// TODO: test that parser produces warnings for immiscible operators

			Action<EcsPrinterOptions> parens = p => p.AllowChangeParentheses = true;
			Action<EcsPrinterOptions> mixImm = p => p.MixImmiscibleOperators = true;
			// Of course, operators can be mixed with themselves.
			Stmt("a + b + c;", F.Call(S.Add, F.Call(S.Add, a, b), c), parens);
			Stmt("a = b = c;", F.Assign(a, F.Assign(b, c)), parens);
			// But some cannot be mixed with each other, unless requested (with mixImm).
			Option(Mode.PrintBothParseFirst, "@`'<<`(a, b) + 1;",    "(a << b) + 1;",   F.Call(S.Add, F.Call(S.Shl, a, b), one), parens);
			Option(Mode.PrintBothParseFirst, "@`'+`(a, b) << 1;",    "(a + b) << 1;",   F.Call(S.Shl, F.Call(S.Add, a, b), one), parens);
			Option(Mode.Both,                "@`'+`(a, b) << 1;",    "a + b << 1;",     F.Call(S.Shl, F.Call(S.Add, a, b), one), mixImm);
			// "@&(a, b) == 1;" would also be acceptable output on the left:
			Option(Mode.PrintBothParseFirst, "a `'&` b == 1;",    "(a & b) == 1;",    F.Call(S.Eq, F.Call(S.AndBits, a, b), one), parens);
			Option(Mode.PrintBothParseFirst, "@`'==`(a, b) & 1;",    "(a == b) & 1;",   F.Call(S.AndBits, F.Call(S.Eq, a, b), one), parens);
			Option(Mode.Both,                "@`'==`(a, b) & 1;",    "a == b & 1;",     F.Call(S.AndBits, F.Call(S.Eq, a, b), one), mixImm);
			Option(Mode.PrintBothParseFirst, "Foo(a, b) + 1;",    "(a `Foo` b) + 1;", F.Call(S.Add, Operator(F.Call(Foo, a, b)), one), parens);
			// @+(a, b) `foo` 1; would also be acceptable output on the left:
			Option(Mode.PrintBothParseFirst, "a `'+` b `Foo` 1;", "(a + b) `Foo` 1;", Operator(F.Call(Foo, F.Call(S.Add, a, b), one)), parens);
		}

		[Test]
		public void UnprintableOperatorNew()
		{
			Expr("new Foo { a }",         F.Call(S.New, F.Call(Foo), a));      // new Foo() { a } would also be ok
			Expr("@'new([x] Foo(), a)",   F.Call(S.New, Attr(x, F.Call(Foo)), a));
			Expr("@'new(([x] Foo)(), a)", F.Call(S.New, F.Call(Attr(x, Foo)), a), Mode.PrinterTest);
			Expr("@'new(Foo, a)",         F.Call(S.New, Foo, a));
			Expr("@'new(Foo)",            F.Call(S.New, Foo));
			Expr("new @`'+`(a, b)",       F.Call(S.New, F.Call(S.Add, a, b))); // #new(@+(a, b)) would also be ok
			Expr("@'new(Foo()(), a)",     F.Call(S.New, F.Call(F.Call(Foo)), a));
			Expr("@'new",                 F.Id(S.New));
			Expr("@'new()",               F.Call(S.New));
			Expr("new { }",               F.Call(S.New, F.Missing));
			Expr("new { a = 1 }",         F.Call(S.New, F.Missing, F.Call(S.Assign, a, one)));
		}

		[Test]
		public void LambdaConstructorChallenge()
		{
			// In EC# mode, the printer produces an empty attribute list here
			// to indicate that parsing as a var decl should be favored...
			LNode intN_x = F.Var(F.Of(S.QuestionMark, F.Int32), x);
			Stmt("public Foo([] int? x) => a;", Attr(@public, F.Call(S.Lambda, F.Call(Foo, intN_x), a)));
			// But in plain C# mode the empty attribute list is illegal and 
			// should be suppressed, while the attribute 'public' should be 
			// preserved so that this "lambda", which C# understands as a 
			// constructor, can be passed through from EC# to LeMP to C#.
			Stmt("public Foo(int? x) => b;", Attr(@public, F.Call(S.Lambda, F.Call(Foo, intN_x), b)),
				opt => opt.SetPlainCSharpMode());
		}

		[Test]
		public void MiscPrinterFocusedTests()
		{
			// Not a type context
			Expr("checked(@`'[]`<int>)",   F.Call(S.Checked,   F.Call(S.Of, _(S.Array), F.Int32)));
			Expr("unchecked(@`'[]`<int>)", F.Call(S.Unchecked, F.Call(S.Of, _(S.Array), F.Int32)));
		}

	}
}
