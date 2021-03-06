using Loyc.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Loyc.Syntax
{
	/// <summary>A central class for keeping track of literal parsers and literal printers.</summary>
	/// <seealso cref="StandardLiteralHandlers"/>
	public class LiteralHandlerTable : ILiteralParser, ILiteralPrinter
	{
		private static Symbol _null = (Symbol)"null";

		public LiteralHandlerTable()
		{
			_parsers = new Dictionary<Symbol, Func<UString, Symbol, Either<object, LogMessage>>>();
			_printers = new Dictionary<object, Func<ILNode, StringBuilder, Either<Symbol, LogMessage>>>();
		}

		Dictionary<Symbol, Func<UString, Symbol, Either<object, LogMessage>>> _parsers;
		Dictionary<object, Func<ILNode, StringBuilder, Either<Symbol, LogMessage>>> _printers;

		/// <summary>A table of parsers indexed by type marker Symbol. 
		/// The <see cref="AddParser"/> method is used to add an item to this collection.</summary>
		public IReadOnlyDictionary<Symbol, Func<UString, Symbol, Either<object, LogMessage>>> Parsers => _parsers;

		/// <summary>A table of printers indexed by Type or by type marker Symbol. 
		/// The <see cref="AddPrinter"/> methods are used to add an item to this collection.</summary>
		public IReadOnlyDictionary<object, Func<ILNode, StringBuilder, Either<Symbol, LogMessage>>> Printers => _printers;

		/// <summary>Adds a parser to the <see cref="Parsers"/> collection.</summary>
		/// <param name="replaceExisting">If the specified type already has a printer 
		/// assigned to it, it will be replaced only if this flag is true.</param>
		/// <param name="typeMarker">The parser will be invoked by <see cref="TryParse"/>
		/// when this type marker Symbol matches the literal being parsed.</param>
		/// <param name="parser">A function that converts a UString to a value (object),
		/// or returns a <see cref="LogMessage"/> if an error occurs. The type marker is
		/// also provided to the parser.</param>
		/// <returns>true if the printer was installed (if replaceExisting is true, 
		/// the method will return true unless the <c>type</c> is null.)</returns>
		public bool AddParser(bool replaceExisting, Symbol typeMarker, Func<UString, Symbol, Either<object, LogMessage>> parser)
		{
			if (typeMarker == null)
				return false;
			lock (_parsers)
			{
				if (!replaceExisting && _parsers.ContainsKey(typeMarker))
					return false;
				_parsers[typeMarker] = parser;
			}
			return true;
		}

		/// <summary>Adds a printer to the <see cref="Printers"/> collection.</summary>
		/// <param name="replaceExisting">If the specified type already has a printer 
		/// assigned to it, it will be replaced only if this flag is true.</param>
		/// <param name="type">The printer will be invoked by <see cref="TryPrint(ILNode, StringBuilder)"/>
		/// when this Type or type marker Symbol matches the literal being printed.</param>
		/// <param name="printer">A printer function that prints into the provided 
		/// StringBuilder and returns null on success, or a description of the error 
		/// that occurred.</param>
		/// <returns>true if the printer was installed (if replaceExisting is true, 
		/// the method will return true unless the <c>type</c> is null.)</returns>
		public bool AddPrinter(bool replaceExisting, Symbol type, Func<ILNode, StringBuilder, Either<Symbol, LogMessage>> printer) => AddPrinter(replaceExisting, (object)type, printer);
		/// <inheritdoc cref="AddPrinter(bool, Symbol, Func{ILNode, Either{UString, LogMessage}})"/>
		public bool AddPrinter(bool replaceExisting, Type type, Func<ILNode, StringBuilder, Either<Symbol, LogMessage>> printer) => AddPrinter(replaceExisting, (object)type, printer);
		private bool AddPrinter(bool replaceExisting, object key, Func<ILNode, StringBuilder, Either<Symbol, LogMessage>> printer)
		{
			if (key == null)
				return false;
			lock (_printers)
			{
				if (!replaceExisting && _printers.ContainsKey(key))
					return false;
				_printers[key] = printer;
			}
			return true;
		}

		/// <summary>Returns true if there is a parser function for the given type marker. Never throws.</summary>
		public bool CanParse(Symbol typeMarker)
		{
			return typeMarker != null && Parsers.ContainsKey(typeMarker);
		}

		/// <summary>Returns true if there is a printer function for the given type marker. Never throws.</summary>
		public bool CanPrint(Symbol typeMarker)
		{
			return typeMarker != null && Printers.ContainsKey(typeMarker);
		}

		bool ILiteralPrinter.CanPrint(Type type) => CanPrint(type);
		/// <summary>Returns true if there is a printer function for the given type. Never throws.</summary>
		/// <param name="searchBases">Whether to search for printers among the base types of the given type.</param>
		/// <returns>True if type is not null and if there is a printer for that type.</returns>
		public bool CanPrint(Type type, bool searchBases = true)
		{
			if (type != null)
			{
				if (Printers.ContainsKey(type))
					return true;
				if (searchBases) {
					for (var type2 = type; type2.BaseType != null; type2 = type2.BaseType)
						if (_printers.ContainsKey(type2.BaseType))
							return true;
					foreach (var iface in type.GetInterfaces())
						if (_printers.ContainsKey(iface))
							return true;
				}
			}
			return false;
		}

		/// <inheritdoc cref="ILiteralParser.TryParse(UString, Symbol)"/>
		public Either<object, ILogMessage> TryParse(UString textValue, Symbol typeMarker)
		{
			typeMarker = typeMarker ?? GSymbol.Empty;
			if (Parsers.TryGetValue(typeMarker, out var parser))
				try {
					return parser(textValue, typeMarker).MapRight(m => (ILogMessage)m);
				} catch (Exception e) {
					return new Either<object, ILogMessage>((ILogMessage) new LogMessage(Severity.Error, textValue, e.Description()));
				}
			return new Either<object, ILogMessage>((ILogMessage) new LogMessage(Severity.Note, textValue, "No parser is registered for type marker '{0}'".Localized(PrintHelpers.EscapeCStyle(typeMarker.Name))));
		}

		public Either<Symbol, ILogMessage> TryPrint(ILNode literal, out StringBuilder sb)
		{
			sb = new StringBuilder();
			return TryPrint(literal, sb);
		}

		public Either<Symbol, ILogMessage> TryPrint(object value, Symbol typeMarker, out StringBuilder sb, NodeStyle style = NodeStyle.Default) =>
			TryPrint(LNode.Literal(SourceRange.Synthetic, new LiteralValue(value, typeMarker), style), out sb);

		/// <summary>Searches <see cref="Printers"/> for a printer for the value and uses it 
		/// to convert the value to a string. When a printer can be found both by type marker
		/// Symbol and by Type, the printer for the matching type marker is used (takes priority).
		/// The complete search order is (1) type marker (if any), (2) exact type, (3) base 
		/// classes and (4) interfaces, in that order.</summary>
		/// <param name="literal">A literal that you want to convert to a string.</param>
		/// <returns>Either the type marker for the literal, or an error message. 
		/// On return, the string form of the literal is appended to the StringBuilder.
		/// If an error occurs, it is possible that some kind of output was added to
		/// the StringBuilder anyway.</returns>
		/// <remarks>
		/// Note: this uses `Type.GetInterfaces` to obtain the list of interfaces, and this 
		/// list is officially not "in a particular order". It appears that the list is 
		/// constructed with some kind of depth-first search, so in simple cases derived 
		/// interfaces are searched before base interfaces, but if there are "diamonds" in
		/// the interface hierarchy then it will not be uncommon for a lower-level interface 
		/// to be checked before a higher-level one. Sorry.
		/// <para/>
		/// If a printer returns an error, this method tries to find other printers that might
		/// be able to print the value. If no printer succeeds, the <i>first</i> error that 
		/// occurred is returned.
		/// <para/>
		/// When the literal is null and there is no printer associated with literal.TypeMarker,
		/// this funtion produces no output and returns literal.TypeMarker.
		/// <para/>
		/// On success, the return value indicates which type marker is recommended based
		/// on the data type of the literal. This is not guaranteed to match the TypeMarker
		/// originally stored in the literal. It is recommended that language printers
		/// use the type marker stored in the literal (regardless of what this method
		/// returns) unless <c>literal.TypeMarker == null</c>.
		/// </remarks>
		public Either<Symbol, ILogMessage> TryPrint(ILNode literal, StringBuilder sb)
		{
			CheckParam.IsNotNull(nameof(sb), sb);
			ILogMessage firstError = null;
			
			var tm = literal.TypeMarker;
			if (tm != null && TryPrint(literal, tm, sb, ref tm, ref firstError))
				return tm;
			
			if (literal.Value == null) {
				return tm;
			}
			
			Type type = literal.Value.GetType();
			if (TryPrint(literal, type, sb, ref tm, ref firstError))
				return tm;

			for (var type2 = type; type2.BaseType != null; type2 = type2.BaseType)
			{
				var @base = type2.BaseType;
				if (TryPrint(literal, @base, sb, ref tm, ref firstError))
					return tm;
			}
			foreach (var iface in type.GetInterfaces())
			{
				if (TryPrint(literal, iface, sb, ref tm, ref firstError))
					return tm;
			}

			return new Either<Symbol, ILogMessage>(firstError ??
				new LogMessage(Severity.Error, literal, 
					"There is no printer for type '{0}'".Localized(literal.Value.GetType())));
		}
		private bool TryPrint(ILNode literal, object key, StringBuilder sb, ref Symbol typeMarker, ref ILogMessage firstError)
		{
			if (_printers.TryGetValue(key, out var printer))
			{
				sb.Clear();
				Either<Symbol, LogMessage> result;
				try {
					result = printer(literal, sb);
				} catch (Exception e) {
					result = new LogMessage(Severity.Error, literal, e.Description());
				}
				if (result.Right.HasValue) {
					firstError = firstError ?? result.Right.Value;
					return false;
				} else {
					typeMarker = result.Left.Value ?? typeMarker;
					return true;
				}
			}
			return false;
		}
	}
}
