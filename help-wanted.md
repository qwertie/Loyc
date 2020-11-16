---
title:  "LeMP: Help Wanted"
layout: article
toc: true
redirectDomain: ecsharp.net
---

Task: make EC# real
-------------------

Like Pinocchio, Enhanced C# wants to be real: not just a single-file generator, but its own proper project type with member completion and all that! Then again, maybe people really do prefer the single-file generator approach - if it's officially a C# project, a "virgin" copy of Visual Studio 2015 will be able to open it - it's just that they just want IntelliSense, and red squiggly underlined errors in the EC# file.

In either case, the first thing we need is someone to help make a Roslyn back-end for LeMP. In other words, I want to convert the output of LeMP into a Microsoft Roslyn syntax tree and compile it with the Roslyn C# 6 compiler. Once that exists, the next step might be to write a Visual Studio extension that introduces a new "Enhanced C# project type" that uses LeMP as the front-end and Roslyn C# as the backend. An EC# project type could allow `*.ecs` files to enjoy IntelliSense just like plain C#! It should also allow mixed C#-EC# projects, in which the *.cs files use Roslyn directly; both file types should be first-class citizens.

I do not have time to do all of this myself, my TO-DO list is full, so if nobody else volunteers, it won't happen. If you want to do this project, I will happily teach you whatever you need to now about LeMP; learning about Roslyn will be your responsibility, and I only know the basics of writing Visual Studio extensions (having written the syntax highlighter for `*.ecs`).

Task: write VB.NET printer
--------------------------

Write a class that prints Enhanced C# Loyc trees as VB.NET code. Since VB.NET and C# have the same type system (including generics), this will mostly be a straightforward (if time-consuming) task. There are a few differences where VB.NET allows something that Enhanced C# doesn't or vice versa; we should discuss these issues on GitHub as they come up.

Once a VB.NET printer exists, two cool things will be possible:

1. In a VB project, you can insert C# code in a .ecs file and it will almost seamlessly integrate with the VB code - the VB code can call the C# code and vice versa, with no need to create a separate assembly for your C# code.
2. You will be able to use all the features of LeMP in your VB project (albeit you'll have to use C# syntax, unless somebody writes a VB parser too... alternately, one could use the Roslyn VB parser and write a converter from Roslyn VB trees to Loyc trees).

The best example of an existing printer is `Les3Printer` (`EcsNodePrinter` is much too complex to base your printer on, and if I were to write it again, there would be some simplifications). Of course, now that `matchCode` exists, your printer can take advantage of it to deconstruct Loyc trees... or you could extend the existing validation code in `EcsValidators` to do deconstruction, too (e.g. extracting the class name and base classes from a class/struct/enum or other so-called "space declaration").

Task: write Javascript or C++ printer
-------------------------------------

Write a class that prints Loyc trees as C++, Javascript or Swift code, so that in the future LLLPG and LeMP can produce code in those languages. Since no one has defined a mapping between Loyc trees and these languages before, the first step is to plan out how each Javascript or C++ or Swift construct will be represented as a Loyc tree.

Sometimes you can just use the same mapping as Enhanced C#, and sometimes you'll have to extend it or modify it. For example, a C# class has a Loyc tree like this:

    class ClassName : BaseClass { Body; }           // EC# code
    `#class`(ClassName, `#`(BaseClass), { Body; }); // LESv3 code

a very similar mapping might work well for C++:

    class ClassName : public BaseClass { Body; };           // C++ code
    `#class`(ClassName, `#`(@public BaseClass), { Body; }); // LESv3 code

but what to do about `ClassName {} Foo;`? One possible solution is to nest the class declaration inside a variable declaration:

    class ClassName : public BaseClass { Body; } Foo;
    #var(#class(ClassName, #(public BaseClass), { Body; }), Foo);

Designing a good and complete mapping could be pretty hard for C++, but Javascript by contrast should be quite straightforward.

Consider [Les3Printer](https://github.com/qwertie/ecsharp/tree/master/Core/Loyc.Syntax/LES/Les3Printer.cs) as a template for how to write a printer.

Task: catalog bugs in EC# parser
--------------------------------

Could someone write a test program that looks for bugs in the Enhanced C# parser?

1. Recursively searches a directory for *.cs files and
2. Parses each one with code like this, printing out all errors to the console:

~~~csharp
var stream = File.Open(path, FileMode.Open, FileAccess.Read)
var chars = new StreamCharSource(stream);
IListSource<LNode> statements = EcsLanguageService.Parse(
   chars, path, MessageSink.Console, ParsingService.File);
~~~

3. Copy all files to a second folder, but with all *.cs files replaced by the output of `EcsLanguageService.Parse` (something like `File.WriteAllText(newPath, EcsLanguageService.Value.Print (chars, MessageSink.Console)`). This way you can try compiling the output, to verify that the printer works properly.

Task: documentation system
--------------------------

XML doc comments are really clunky (I can't use `&`? Can't write `List<T>`?!), and the tools for making documentation out of them are equally clunky. Doxygen isn't a great alternative either, since it often misinterprets XML doc comments by assuming you really wanted to use doxygen parsing rules (sometimes an `@` is just an `@`). 

We should allow developers to transition to a new doc comment system by supporting _both_ xml doc comments, and a new system of some kind, perhaps one based on Markdown.

Now that comments are associated with nodes in the syntax tree, the next step is to write a program that scans a directory full of source files and either

1. Builds a tree of namespaces, classes, methods, fields, properties, and events; this tree can be used for lookup by the documentation system, but it would also help with other code analysis tasks.
2. Alternately, if someone writes a Roslyn back-end, Roslyn will build its own tree of namespaces, classes, methods, fields, properties, and events.

Once this tree exists, you can scan it for doc comments and produce nice HTML and/or Markdown output.

Task: talk to the lonely guy
----------------------------

What's missing from Enhanced C#? Can that feature be done with a macro? Tell me about your ideas or any macros you've made. My email address is on the [home page](/).

P.S. a shout out to the [srclib](https://srclib.org/) project. I wish I had time to implement the Visual Studio version!