﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reactive.Linq;
using System.Drawing.Drawing2D;
using Loyc;
using Loyc.Collections;
using Loyc.Math;
using System.Diagnostics;
using System.Reactive;
using Util.WinForms;
using Util.UI;
using Loyc.Geometry;
using Coord = System.Single;
using LineSegmentT = Loyc.Geometry.LineSegment<float>;
using PointT = Loyc.Geometry.Point<float>;
using VectorT = Loyc.Geometry.Vector<float>;
using System.IO;
using Loyc.Collections.Impl;

namespace BoxDiagrams
{
	// "Baadia": Boxes And Arrows Diagrammer
	//
	// Future flourishes:
	// - linear gradient brushes (modes: gradient across shape, or gradient across sheet)
	// - sheet background pattern/stretch bitmap
	// - box background pattern/stretch bitmap
	// - snap lines, plus a ruler on top and left to create and remove them
	//   - ruler itself can act as scroll bar
	// - text formatting override for parts of a box
	// - change to proper model-view-viewmodel pattern so that the program can easily be
	//   ported to Android and iOS via Xamarin tools (also allows multiple views of one
	//   document)
	//
	// Gestures for drawing shapes
	// ---------------------------
	// 1. User can back up the line he's drawing (DONE)
	// 2. Line or arrow consisting of straight segments (DONE)
	// 2b. User can start typing to add text above the line (VERY RUDIMENTARY)
	// 2c. User-customized angles: 45, 30/60, 30/45/60, 15/30/45/60/75
	// 3. Box detected via two to four straight-ish segments (DONE)
	// 4. Ellipse detected by similarity to ideal ellipse, angles
	// 3b/4b. User can start typing to add text in the box/ellipse (BASIC VERSION)
	// 5. Closed shape consisting of straight segments
	// 5b. User can start typing to add text in the closed shape,
	//    which is treated as if it were a rectangle.
	// 6. Free-form line by holding Alt or by poor fit to straight-line model
	// 7. Free-form closed shape by holding Alt or by poor fit to straight-line
	//    and ellipse models
	// 8. Scribble-erase detected by repeated pen reversal (DONE)
	// 9. Scribble-cancel current shape (DONE)
	// 10. Click empty space and type to create borderless text (DONE)
	// 11. Double-click empty space to create a marker (DONE)
	// 
	// Other behavior
	// --------------
	// 1. Ctrl+Z for unlimited undo (Ctrl+Y/Ctrl+Shift+Z for redo) (BASIC VERSION)
	// 2. Click and type for freefloating text with half-window default wrap width
	// 3. Click a shape to select it; Ctrl+Click for multiselect (DONE)
	// 4  Double-click near an endpoint of a selected polyline/curve to change 
	//    arrow type (DONE, except you must click the endpoint square)
	// 5. Double-click a polyline or curve to toggle curve-based display; 
	// 6. Double-click a text box to cycle between box, ellipse, and borderless (DONE)
	// 5. Double-click a free-form shape to simplify and make it curvy, 
	//    double-click again for simplified line string.
	// 6. When adding text to a textbox, its height increases automatically when 
	//    space runs out.
	// 7. Long-press or right-click to show style menu popup 
	// 8. When clicking to select a shape, that shape's DrawStyle becomes the 
	//    active style for new shapes also (does not affect arrowheads)
	// 9. When nothing is selected and user clicks and drags, this can either
	//    move the shape under the cursor or it can draw a new shape. Normally
	//    the action will be to draw a new shape, but when the mouse is clearly
	//    within a non-panel textbox, the textbox moves (the mouse cursor reflects
	//    the action that will occur). (DONE)
	// 10. When moving a box, midpoints of attached free-form lines/arrows will 
	//     move in proportion to how close those points are to the anchored 
	//     endpoint, e.g. a midpoint at the halfway point of its line/arrow will 
	//     move at 50% of the speed of the box being moved, assuming that 
	//     line/arrow is anchored to the box.
	// 11. When moving a box, midpoints of attached non-freeform arrows...
	//     well, it's complicated. (DONE)
	// 11b. Non-freeform lines truncate themselves at the edges of a box they are 
	//     attached to.
	// 13. Automatic Z-order. Larger textboxes are always underneath smaller ones.
	//     Free lines are on top of all boxes. Anchored lines are underneath 
	//     their boxes. (DONE)

	/// <summary>A control that manages a set of <see cref="Shape"/> objects and 
	/// manages a mouse-based user interface for drawing things.</summary>
	/// <remarks>
	/// This class has the following responsibilities: TODO
	/// </remarks>
	public partial class DiagramControl : DrawingControlBase
	{
		public DiagramControl()
		{
			_mainLayer = Layers[0]; // predefined
			_selAdorners = AddLayer(false);
			_dragAdorners = AddLayer(false);
			
			Document = new DiagramDocument();
			
			LineStyle = new DiagramDrawStyle { LineColor = Color.Black, LineWidth = 2, TextColor = Color.Blue, FillColor = Color.FromArgb(64, Color.Gray) };
			LineStyle.Name = "Default";
			BoxStyle = (DiagramDrawStyle)LineStyle.Clone();
			BoxStyle.LineColor = Color.DarkGreen;
			MarkerRadius = 5;
			MarkerType = MarkerPolygon.Circle;
			FromArrow = null;
			ToArrow = Arrowhead.Arrow30deg;
		}

		// This oversized attribute tells the WinForms designer to ignore the property
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public DiagramDrawStyle LineStyle { get; set; }
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public DiagramDrawStyle BoxStyle { get; set; }
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public MarkerPolygon MarkerType { get; set; }
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Arrowhead FromArrow { get; set; }
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Arrowhead ToArrow { get; set; }

		public float MarkerRadius { get; set; }

		LLShapeLayer _mainLayer;          // shows current Document (lowest layer)
		LLShapeLayer _selAdorners;        // shows adornments for selected shapes
		LLShapeLayer _dragAdorners;       // shows drag line and a shape to potentially create
		Point<float>? _lastClickLocation; // used to let user click blank space and start typing

		void RecreateSelectionAdorners()
		{
			_selAdorners.Shapes.Clear();

			_selectedShapes.IntersectWith(_doc.Shapes);
			foreach (var shape in _selectedShapes)
				shape.AddAdornersTo(_selAdorners.Shapes, shape == _partialSelShape ? SelType.Partial : SelType.Yes, HitTestRadius);
			_selAdorners.Invalidate();
		}

		#region Commands and keyboard input handling

		static Symbol S(string s) { return GSymbol.Get(s); }
		public Dictionary<Pair<Keys, Keys>, Symbol> KeyMap = new Dictionary<Pair<Keys, Keys>, Symbol>()
		{
			{ Pair.Create(Keys.Z, Keys.Control), S("Undo") },
			{ Pair.Create(Keys.Y, Keys.Control), S("Redo") },
			{ Pair.Create(Keys.Z, Keys.Control | Keys.Shift), S("Redo") },
			{ Pair.Create(Keys.A, Keys.Control), S("SelectAll") },
			{ Pair.Create(Keys.Delete, (Keys)0), S("DeleteSelected") },
			{ Pair.Create(Keys.Delete, Keys.Control), S("ClearText") },
			{ Pair.Create(Keys.X, Keys.Control), S("Cut") },
			{ Pair.Create(Keys.C, Keys.Control), S("Copy") },
			{ Pair.Create(Keys.V, Keys.Control), S("Paste") },
			{ Pair.Create(Keys.Delete, Keys.Shift), S("Cut") },
			{ Pair.Create(Keys.Insert, Keys.Control), S("Copy") },
			{ Pair.Create(Keys.Insert, Keys.Shift), S("Paste") },
		};
		
		Map<Symbol, ICommand> _commands;
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Map<Symbol, ICommand> Commands
		{
			get { 
				return _commands = _commands ?? 
				    (((Map<Symbol, ICommand>)CommandAttribute.GetCommandMap(this))
				                      .Union(CommandAttribute.GetCommandMap(_doc.UndoStack)));
			}
		}

		public bool ProcessShortcutKey(KeyEventArgs e)
		{
			Symbol name;
			if (KeyMap.TryGetValue(Pair.Create(e.KeyCode, e.Modifiers), out name)) {
				ICommand cmd;
				if (Commands.TryGetValue(name, out cmd) && cmd.CanExecute) {
					cmd.Run();
					return true;
				}
			}
			return false;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (!(e.Handled = e.Handled || ProcessShortcutKey(e)))
				if (_focusShape != null)
					_focusShape.OnKeyDown(e);
		}
		protected override void OnKeyUp(KeyEventArgs e)
		{
			base.OnKeyUp(e);
			if (!e.Handled && _focusShape != null)
				_focusShape.OnKeyUp(e);
		}
		protected override void OnKeyPress(KeyPressEventArgs e)
		{
			base.OnKeyPress(e);
			if (!e.Handled) {
				// Should we add text to _focusShape or create a new text shape?
				bool ignorePanel = false;
				if (_focusShape != null && _focusShape.IsPanel && string.IsNullOrEmpty(_focusShape.PlainText()))
					ignorePanel = true;
				if (_focusShape != null && !ignorePanel) {
					_focusShape.OnKeyPress(e);
				} else if (e.KeyChar >= 32 && _lastClickLocation != null) {
					var pt = _lastClickLocation.Value;
					int w = MathEx.InRange(Width / 4, 100, 400);
					int h = MathEx.InRange(Height / 8, 50, 200);
					var newShape = new TextBox(new BoundingBox<float>(pt.X - w / 2, pt.Y, pt.X + w / 2, pt.Y + h)) {
						Text = e.KeyChar.ToString(),
						BoxType = BoxType.Borderless,
						TextJustify = LLTextShape.JustifyUpperCenter,
						Style = BoxStyle
					};
					AddShape(newShape);
				}
			}
		}

		[Command(null, "Delete selected shapes")]
		public bool DeleteSelected(bool run = true)
		{
			if (_partialSelShape == null && _selectedShapes.Count == 0)
				return false;
			if (run) {
				if (_partialSelShape != null)
					_selectedShapes.Add(_partialSelShape);
				DeleteShapes((Set<Shape>)_selectedShapes);
			}
			return true;
		}

		#endregion

		#region Mouse input handling - general
		// The base class gathers mouse events and calls AnalyzeGesture()

		new public class DragState : DrawingControlBase.DragState
		{
			public DragState(DiagramControl c, MouseEventArgs down) : base(c, down) { Control = c; }
			public new DiagramControl Control;
			public Util.UI.UndoStack UndoStack { get { return Control._doc.UndoStack; } }
			public VectorT HitTestRadius { get { return Control.HitTestRadius; } }
			public Shape StartShape;
			public IEnumerable<Shape> NearbyShapes { get { return Control.ShapesOnScreen(Points.Last.Point); } }

			bool _gotAnchor;
			Anchor _startAnchor;
			public Anchor StartAnchor
			{
				get {
					if (!_gotAnchor)
						if (Points.Count > 1) {
							_gotAnchor = true;
							_startAnchor = Control.GetBestAnchor(Points[0].Point, Points[1].AngleMod8);
						}
					return _startAnchor;
				}
			}

			public HitTestResult ClickedShape;
		}

		protected override bool AddFiltered(DrawingControlBase.DragState state_, DragPoint dp)
		{
			DragState state = (DragState)state_;
			if (state.ClickedShape != null && state.ClickedShape.AllowsDrag)
				return false; // gesture recognition is off
			return base.AddFiltered(state, dp);
		}

		protected override DrawingControlBase.DragState MouseClickStarted(MouseEventArgs e)
		{
			var htresult = HitTest((PointT)e.Location.AsLoyc());
			if (htresult != null && htresult.AllowsDrag 
				&& !_selectedShapes.Contains(htresult.Shape) 
				&& (Control.ModifierKeys & Keys.Control) == 0)
				ClickSelect(htresult.Shape);
			return new DragState(this, e) {
				ClickedShape = htresult, 
			};
		}

		protected VectorT HitTestRadius = new VectorT(8, 8);
		private readonly DiagramDrawStyle SelectorBoxStyle = new DiagramDrawStyle { 
			Name = "(Temporary selection indicator)",
			LineColor = Shape.SelAdornerStyle.LineColor, LineStyle = DashStyle.Dash, 
			FillColor = Shape.SelAdornerStyle.FillColor,
			LineWidth = Shape.SelAdornerStyle.LineWidth
		};

		// most recently drawn shape is "partially selected". Must also be in _selectedShapes
		protected Shape _partialSelShape;
		protected Shape _focusShape; // most recently clicked or created (gets keyboard input)
		protected MSet<Shape> _selectedShapes = new MSet<Shape>();

		protected SelType GetSelType(Shape shape)
		{
			if (shape == _partialSelShape)
				return SelType.Partial;
			return _selectedShapes.Contains(shape) ? SelType.Yes : SelType.No;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (_dragState != null)
				return;
			var mouseLoc = (PointT)e.Location.AsLoyc();
			var result = HitTest(mouseLoc);
			Cursor = result != null ? result.MouseCursor : Cursors.Cross;
		}

		[Command(null, "Duplicate")]
		public bool DuplicateSelected(bool run = true)
		{
			if (_selectedShapes.Count == 0)
				return false;
			if (run) {
				// Equivalent to copy + paste
				var buf = SerializeSelected();
				buf.Position = 0;
				PasteAndSelect(buf, new VectorT(20, 20));
			}
			return true;
		}

		[Command(null, "Copy")]
		public bool Cut(bool run = true)
		{
			if (Copy(run)) {
				DeleteSelected(run);
				return true;
			}
			return false;
		}

		[Command(null, "Copy")]
		public bool Copy(bool run = true)
		{
			if (_selectedShapes.Count == 0)
				return false;
			if (run) {
				var buf = SerializeSelected();
				var data = new DataObject();

				data.SetData("DiagramDocument", buf.ToArray());

				var sortedShapes = _selectedShapes.OrderBy(s => {
					var c = s.BBox.Center();
					return c.Y + c.X / 10;
				});
				var text = StringExt.Join("\n\n", sortedShapes
					.Select(s => s.PlainText()).Where(t => !string.IsNullOrEmpty(t)));
				if (!string.IsNullOrEmpty(text))
					data.SetText(text);

				// Crazy Clipboard deletes data by default on app exit!
				// need 'true' parameter to prevent loss of data on exit
				Clipboard.SetDataObject(data, true); 
			}
			return true;
		}

		[Command(null, "Paste")]
		public bool Paste(bool run = true)
		{
			if (Clipboard.ContainsData("DiagramDocument")) {
				if (run) {
					var buf = Clipboard.GetData("DiagramDocument") as byte[];
					if (buf != null)
						PasteAndSelect(new MemoryStream(buf), VectorT.Zero);
				}
				return true;
			} else if (Clipboard.ContainsText()) {
				if (run) {
					var text = Clipboard.GetText();

					DoOrUndo act = null;
					if (_focusShape != null && (act = _focusShape.AppendTextAction(text)) != null)
						_doc.UndoStack.Do(act, true);
					else {
						var textBox = new TextBox(new BoundingBox<Coord>(0, 0, 300, 200)) { 
							Text = text, TextJustify = LLTextShape.JustifyMiddleCenter, 
							BoxType = BoxType.Borderless, Style = BoxStyle
						};
						_doc.AddShape(textBox);
					}
				}
				return true;
			}
			return false;
		}

		[Command(null, "Clear text")]
		public bool ClearText(bool run = true)
		{
			bool success = false;
			foreach (var shape in _selectedShapes) {
				var act = shape.GetClearTextAction();
				if (act != null) {
					success = true;
					if (run)
						_doc.UndoStack.Do(act, false);
				}
			}
			_doc.UndoStack.FinishGroup();
			return success;
		}

		private MemoryStream SerializeSelected()
		{
			var doc = new DiagramDocumentCore();
			doc.Shapes.AddRange(_selectedShapes);
			// no need to populate doc.Styles, it is not used for copy/paste
			var buf = new MemoryStream();
			doc.Save(buf);
			return buf;
		}

		private DiagramDocumentCore PasteAndSelect(Stream buf, VectorT offset)
		{
			var doc = DeserializeAndEliminateDuplicateStyles(buf);
			foreach(var shape in doc.Shapes)
				shape.MoveBy(offset);
			_doc.MergeShapes(doc);
			return doc;
		}

		private DiagramDocumentCore DeserializeAndEliminateDuplicateStyles(Stream buf)
		{
			var doc = DiagramDocumentCore.Load(buf);
			doc.Styles.Clear();
			foreach (var shape in doc.Shapes) {
				var style = _doc.Styles.Where(s => s.Equals(shape.Style)).FirstOrDefault();
				if (style != null)
					shape.Style = style;
				else
					doc.Styles.Add(shape.Style);
			}
			return doc;
		}

		private Util.WinForms.HitTestResult HitTest(PointT mouseLoc)
		{
			Util.WinForms.HitTestResult best = null;
			bool bestSel = false;
			foreach (Shape shape in ShapesOnScreen(mouseLoc))
			{
				var result = shape.HitTest(mouseLoc, HitTestRadius, GetSelType(shape));
				if (result != null) {
					bool resultSel = _selectedShapes.Contains(result.Shape);
					// Prefer to hit test against an already-selected shape (unless 
					// it's a panel), otherwise the thing with the highest Z-order.
					if (result.Shape.IsPanel)
						resultSel = false;
					if (best == null || (resultSel && !bestSel) || (bestSel == resultSel && best.Shape.HitTestZOrder < result.Shape.HitTestZOrder)) {
						best = result;
						bestSel = resultSel;
					}
				}
			}
			return best;
		}

		// TODO optimization: return a cached subset rather than all shapes
		public IEnumerable<Shape> ShapesOnScreen(PointT mousePos) { return _doc.Shapes; }

		const int MinDistBetweenDragPoints = 2;

		static readonly DrawStyle MouseLineStyle = new DrawStyle { LineColor = Color.FromArgb(96, Color.Gray), LineWidth = 10 };
		static readonly DrawStyle EraseLineStyle = new DrawStyle { LineColor = Color.FromArgb(128, Color.White), LineWidth = 10 };

		protected override void AnalyzeGesture(DrawingControlBase.DragState state_, bool mouseUp)
		{
			// TODO: Analyze on separate thread and maybe even draw on a separate thread.
			//       Otherwise, on slow computers, mouse input may be missed or laggy due to drawing/analysis
			DragState state = (DragState)state_;
			var adorners = _dragAdorners.Shapes;
			adorners.Clear();
			Shape newShape = null;
			IEnumerable<Shape> eraseSet = null;
			
			if (state.IsDrag)
			{
				if (state.ClickedShape != null && state.ClickedShape.AllowsDrag)
					HandleShapeDrag(state);
				else {
					List<PointT> simplified;
					bool cancel;
					eraseSet = RecognizeScribbleForEraseOrCancel(state, out cancel, out simplified);
					if (eraseSet != null) {
						ShowEraseDuringDrag(state, adorners, eraseSet, simplified, cancel);
					} else {
						bool potentialSelection = false;
						newShape = DetectNewShapeDuringDrag(state, adorners, out potentialSelection);
						if (potentialSelection) {
							var selecting = ShapesInside(newShape.BBox).ToList();
							if (selecting.Count != 0)
								newShape.Style = SelectorBoxStyle;
						}
					}
				}
			}

			if (mouseUp) {
				adorners.Clear();
				HandleMouseUp(state, newShape, eraseSet);
			} else if (newShape != null) {
				newShape.AddLLShapesTo(adorners);
				newShape.Dispose();
			}

			_dragAdorners.Invalidate();
		}

		private void HandleShapeDrag(DragState state)
		{
			_doc.UndoStack.UndoTentativeAction();

			var movingShapes = _selectedShapes;
			var panels = _selectedShapes.Where(s => s.IsPanel);
			if (panels.Any() && (_selectedShapes.Count > 1 || 
				state.ClickedShape.MouseCursor == Cursors.SizeAll))
			{
				// Also move shapes that are inside the panel
				movingShapes = _selectedShapes.Clone();
				foreach (var panel in panels)
					movingShapes.AddRange(ShapesInsidePanel(panel));
			}

			if (movingShapes.Count <= 1)
			{
				var shape = state.ClickedShape.Shape;
				DoOrUndo action = shape.DragMoveAction(state.ClickedShape, state.TotalDelta);
				if (action != null) {
					_doc.UndoStack.DoTentatively(action);
					AutoHandleAnchorsChanged();
				}
			}
			else
			{
				foreach (var shape in movingShapes) {
					DoOrUndo action = shape.DragMoveAction(state.ClickedShape, state.TotalDelta);
					if (action != null)
						_doc.UndoStack.DoTentatively(action);
				}
				AutoHandleAnchorsChanged();
			}
		}

		private IEnumerable<Shape> ShapesInsidePanel(Shape panel) { return ShapesInside(panel.BBox, panel); }
		private IEnumerable<Shape> ShapesInside(BoundingBox<Coord> bbox, Shape panel = null)
		{
			foreach (var shape in _doc.Shapes) {
				if (shape != panel && bbox.Contains(shape.BBox))
					yield return shape;
			}
		}

		private void AutoHandleAnchorsChanged()
		{
			foreach (var shape in _doc.Shapes) {
				var changes = shape.AutoHandleAnchorsChanged();
				if (changes != null)
					foreach (var change in changes)
						_doc.UndoStack.DoTentatively(change);
			}
		}

		private void HandleMouseUp(DragState state, Shape newShape, IEnumerable<Shape> eraseSet)
		{
			if (!state.IsDrag) {
				if (state.Clicks >= 2) {
					if (_selectedShapes.Count != 0) {
						var htr = state.ClickedShape;
						foreach (var shape in _selectedShapes) {
							DoOrUndo action = shape.DoubleClickAction(htr.Shape == shape ? htr : null);
							if (action != null)
								_doc.UndoStack.Do(action, false);
						}
						_doc.UndoStack.FinishGroup();
					} else {
						// Create marker shape
						newShape = new Marker(BoxStyle, state.UnfilteredPoints.First().Point, MarkerRadius, MarkerType);
					}
				} else {
					ClickSelect(state.ClickedShape != null ? state.ClickedShape.Shape : null);
					_lastClickLocation = state.UnfilteredPoints.First.Point;
				}
			}

			_doc.UndoStack.AcceptTentativeAction(); // if any
			_partialSelShape = null;
			if (newShape != null) {
				if (newShape.Style == SelectorBoxStyle)
					SelectByBox(newShape.BBox);
				else
					AddShape(newShape);
			}
			if (eraseSet != null)
				DeleteShapes(new Set<Shape>(eraseSet));
			_doc.MarkPanels();
		}

		private void SelectByBox(BoundingBox<Coord> bbox)
		{
			_selectedShapes.Clear();
			_selectedShapes.AddRange(ShapesInside(bbox));
			RecreateSelectionAdorners();
		}
		private void ClickSelect(Shape clickedShape)
		{
			if ((Control.ModifierKeys & Keys.Control) == 0)
				_selectedShapes.Clear();
			if ((_focusShape = clickedShape) != null)
				_selectedShapes.Toggle(_focusShape);
			RecreateSelectionAdorners();
		}

		private void AddShape(Shape newShape)
		{
			_doc.AddShape(newShape);
		}

		void DeleteShapes(Set<Shape> eraseSet)
		{
			_doc.RemoveShapes(eraseSet);
		}

		void AfterShapesRemoved(IReadOnlyCollection<Shape> eraseSet)
		{
			MSet<LLShape> eraseSetLL = new MSet<LLShape>();
			foreach (var s in eraseSet)
				s.AddLLShapesTo(eraseSetLL);
			BeginRemoveAnimation(eraseSetLL);
		}


		private void ShowEraseDuringDrag(DragState state, MSet<LLShape> adorners, IEnumerable<Shape> eraseSet, List<PointT> simplified, bool cancel)
		{
			EraseLineStyle.LineColor = Color.FromArgb(128, BackColor);
			var eraseLine = new LLPolyline(EraseLineStyle, simplified);
			adorners.Add(eraseLine);

			if (cancel) {
				eraseLine.Style = LineStyle;
				BeginRemoveAnimation(adorners);
				adorners.Clear();
				state.IsComplete = true;
			} else {
				// Show which shapes are erased by drawing them in the background color
				foreach (Shape s in eraseSet) {
					Shape s_ = s.Clone();
					s_.Style = (DiagramDrawStyle)s.Style.Clone();
					s_.Style.FillColor = s_.Style.LineColor = s_.Style.TextColor = Color.FromArgb(192, BackColor);
					// avoid an outline artifact, in which color from the edges of the 
					// original shape bleeds through by a variable amount that depends 
					// on subpixel offsets.
					s_.Style.LineWidth++;
					s_.AddLLShapesTo(adorners);
				}
			}
		}
		private Shape DetectNewShapeDuringDrag(DragState state, MSet<LLShape> adorners, out bool potentialSelection)
		{
			potentialSelection = false;
			Shape newShape = null;
			adorners.Add(new LLPolyline(MouseLineStyle, state.Points.Select(p => p.Point).AsList()) { ZOrder = 0x100 });

			if (state.Points.Count == 1)
			{
				newShape = new Marker(BoxStyle, state.Points[0].Point, MarkerRadius, MarkerType);
			}
			else if (state.Points.Count > 1)
			{
				#if DEBUG
				var ss = BreakIntoSections(state);
				EliminateTinySections(ss, 10 + (int)(ss.Sum(s => s.Length) * 0.05));
				foreach (Section s in ss)
					adorners.Add(new LLMarker(new DrawStyle { LineColor = Color.Gainsboro, FillColor = Color.Gray }, s.StartPt, 5, MarkerPolygon.Circle));
				#endif

				newShape = RecognizeBoxOrLines(state, out potentialSelection);
			}
			return newShape;
		}

		Timer _cancellingTimer = new Timer { Interval = 30 };

		private void BeginRemoveAnimation(MSet<LLShape> erasedShapes)
		{
			var cancellingShapes = erasedShapes.Select(s => Pair.Create(s, s.Opacity)).ToList();
			var cancellingTimer = new Timer { Interval = 30, Enabled = true };
			var cancellingLayer = AddLayer();
			cancellingLayer.Shapes.AddRange(erasedShapes);
			int opacity = 255;
			cancellingTimer.Tick += (s, e) =>
			{
				opacity -= 32;
				if (opacity > 0) {
					foreach (var pair in cancellingShapes)
						pair.A.Opacity = (byte)(pair.B * opacity >> 8);
					cancellingLayer.Invalidate();
				} else {
					DisposeLayerAt(Layers.IndexOf(cancellingLayer));
					cancellingTimer.Dispose();
					cancellingLayer.Dispose();
				}
			};
		}

		static bool IsDrag(IList<DragPoint> dragSeq)
		{
			Point<float> first = dragSeq[0].Point;
			Size ds = SystemInformation.DragSize;
			return dragSeq.Any(p => {
				var delta = p.Point.Sub(first);
				return Math.Abs(delta.X) > ds.Width || Math.Abs(delta.Y) > ds.Height;
			});
		}

		static VectorT V(float x, float y) { return new VectorT(x, y); }
		static readonly VectorT[] Mod8Vectors = new[] { 
			V(1, 0), V(1, 1),
			V(0, 1), V(-1, 1),
			V(-1, 0), V(-1, -1),
			V(0, -1), V(1, -1),
		};
		
		static int AngleMod8(VectorT v)
		{
			return (int)Math.Round(v.Angle() * (4 / Math.PI)) & 7;
		}

		#endregion

		#region Mouse input handling - RecognizeBoxOrLines and its helper methods

		Shape RecognizeBoxOrLines(DragState state, out bool potentialSelection)
		{
			var pts = state.Points;
			// Okay so this is a rectangular recognizer that only sees things at 
			// 45-degree angles.
			List<Section> sections1 = BreakIntoSections(state);
			List<Section> sections2 = new List<Section>(sections1);
			
			// Figure out if a box or a line string is a better interpretation
			EliminateTinySections(sections1, 10);
			LineOrArrow line = InterpretAsPolyline(state, sections1);
			Shape shape = line;
			// Conditions to detect a box:
			// 0. If both endpoints are anchored, a box cannot be formed.
			// continued below...
			EliminateTinySections(sections2, 10 + (int)(sections1.Sum(s => s.Length) * 0.05));
			if (line.ToAnchor == null || line.FromAnchor == null || line.FromAnchor.Equals(line.ToAnchor))
				shape = (Shape)TryInterpretAsBox(sections2, (line.FromAnchor ?? line.ToAnchor) != null, out potentialSelection) ?? line;
			else
				potentialSelection = false;
			return shape;
		}

		static int TurnBetween(Section a, Section b)
		{
			return (b.AngleMod8 - a.AngleMod8) & 7;
		}
		private TextBox TryInterpretAsBox(List<Section> sections, bool oneSideAnchored, out bool potentialSelection)
		{
			potentialSelection = false;
			// Conditions to detect a box (continued):
			// 1. If one endpoint is anchored, 4 sides are required to confirm 
			//    that the user really does want to create a (non-anchored) box.
			// 2. There are 2 to 4 points.
			// 3. The initial line is vertical or horizontal.
			// 4. The rotation between all adjacent lines is the same, either 90 
			//    or -90 degrees
			// 5. If there are two lines, the endpoint must be down and right of 
			//    the start point (this is also a potential selection box)
			// 6. The dimensions of the box enclose the first three lines. The 
			//    endpoint of the fourth line, if any, must not be far outside the 
			//    box.
			int minSides = oneSideAnchored ? 4 : 2;
			if (sections.Count >= minSides && sections.Count <= 5) {
				int turn = TurnBetween(sections[0], sections[1]);
				if ((sections[0].AngleMod8 & 1) == 0 && (turn == 2 || turn == 6))
				{
					for (int i = 1; i < sections.Count; i++)
						if (TurnBetween(sections[i - 1], sections[i]) != turn)
							return null;
					
					VectorT dif;
					if (sections.Count == 2)
						potentialSelection = (dif = sections[1].EndPt.Sub(sections[0].StartPt)).X > 0 && dif.Y > 0;
					if (sections.Count > 2 || potentialSelection) {
						var extents = sections.Take(3).Select(s => s.StartPt.To(s.EndPt).ToBoundingBox()).Union();
						if (sections.Count < 4 || extents.Inflated(20, 20).Contains(sections[3].EndPt)) {
							// Confirmed, we can interpret as a box
							return new TextBox(extents) { Style = BoxStyle };
						}
					}
				}
			}
			return null;
		}

		private LineOrArrow InterpretAsPolyline(DragState state, List<Section> sections)
		{
			var shape = new LineOrArrow { Style = LineStyle };
			shape.FromAnchor = state.StartAnchor;
			LineSegmentT prevLine = new LineSegmentT(), curLine;

			for (int i = 0; i < sections.Count; i++) {
				int angleMod8 = sections[i].AngleMod8;
				var startPt = sections[i].StartPt;
				var endPt = sections[i].EndPt;

				Vector<float> vector = Mod8Vectors[angleMod8];
				Vector<float> perpVector = vector.Rot90();

				bool isStartLine = i == 0;
				bool isEndLine = i == sections.Count - 1;
				if (isStartLine) {
					if (shape.FromAnchor != null)
						startPt = shape.FromAnchor.Point;
				}
				if (isEndLine) {
					if ((shape.ToAnchor = GetBestAnchor(endPt, angleMod8 + 4)) != null)
						endPt = shape.ToAnchor.Point;
					// Also consider forming a closed shape
					else if (shape.Points.Count > 1 
						&& shape.Points[0].Sub(endPt).Length() <= AnchorSnapDistance 
						&& Math.Abs(vector.Cross(shape.Points[1].Sub(shape.Points[0]))) > 0.001f)
						endPt = shape.Points[0];
				}

				if (isStartLine)
					curLine = startPt.To(startPt.Add(vector));
				else {
					curLine = endPt.Sub(vector).To(endPt);
					PointT? itsc = prevLine.ComputeIntersection(curLine, LineType.Infinite);
					if (itsc.HasValue)
						startPt = itsc.Value;
				}

				shape.Points.Add(startPt);

				if (isEndLine) {
					if (isStartLine) {
						Debug.Assert(shape.Points.Count == 1);
						var adjustedStart = startPt.ProjectOntoInfiniteLine(endPt.Sub(vector).To(endPt));
						var adjustedEnd = endPt.ProjectOntoInfiniteLine(curLine);
						if (shape.FromAnchor != null) {
							if (shape.ToAnchor != null) {
								// Both ends anchored => do nothing, allow unusual angle
							} else {
								// Adjust endpoint to maintain angle
								endPt = adjustedEnd;
							}
						} else {
							if (shape.ToAnchor != null)
								// End anchored only => alter start point
								shape.Points[0] = adjustedStart;
							else {
								// Neither end anchored => use average line
								shape.Points[0] = startPt.To(adjustedStart).Midpoint();
								endPt = endPt.To(adjustedEnd).Midpoint();
							}
						}
					}
					shape.Points.Add(endPt);
				}
				prevLine = curLine;
			}

			shape.FromArrow = FromArrow;
			shape.ToArrow = ToArrow;

			return shape;
		}

		/// <summary>Used during gesture recognition to represent a section of 
		/// mouse input that is being interpreted as single a line segment.</summary>
		class Section
		{ 
			public int AngleMod8; 
			public PointT StartPt, EndPt;
			public VectorT Vector() { return EndPt.Sub(StartPt); }
			public int iStart, iEnd; 
			public float Length;
			
			public override string ToString()
			{
				return string.Format("a8={0}, Len={1}, indexes={2}..{3}", AngleMod8, Length, iStart, iEnd); // for debug
			}
		}

		static List<Section> BreakIntoSections(DragState state)
		{
			var list = new List<Section>();
			var pts = state.Points;
			int i = 1, j;
			for (; i < pts.Count; i = j) {
				int angleMod8 = pts[i].AngleMod8;
				float length = pts[i - 1].Point.To(pts[i].Point).Length();
				for (j = i + 1; j < pts.Count; j++) {
					if (pts[j].AngleMod8 != angleMod8)
						break;
					length += pts[j - 1].Point.To(pts[j].Point).Length();
				}
				var startPt = pts[i - 1].Point;
				var endPt = pts[j - 1].Point;
				list.Add(new Section { 
					AngleMod8 = angleMod8, 
					StartPt = startPt, EndPt = endPt, 
					iStart = i - 1, iEnd = j - 1,
					Length = length
				});
			}
			return list;
		}
		static void EliminateTinySections(List<Section> list, int minLineLength)
		{
			// Eliminate tiny sections
			Section cur;
			int i;
			while ((cur = list[i = list.IndexOfMin(s => s.Length)]).Length < minLineLength)
			{
				var prev = list.TryGet(i - 1, null);
				var next = list.TryGet(i + 1, null);
				if (PickMerge(ref prev, cur, ref next)) {
					if (prev != null)
						list[i - 1] = prev;
					if (next != null)
						list[i + 1] = next;
					list.RemoveAt(i);
				} else
					break;
			}

			// Merge adjacent sections that now have the same mod-8 angle
			for (i = 1; i < list.Count; i++) {
				Section s0 = list[i-1], s1 = list[i];
				if (s0.AngleMod8 == s1.AngleMod8) {
					s0.EndPt = s1.EndPt;
					s0.iEnd = s1.iEnd;
					s0.Length += s1.Length;
					list.RemoveAt(i);
					i--;
				}
			}
		}
		static double AngleError(VectorT vec, int angleMod8)
		{
			double dif = vec.Angle() - angleMod8 * (Math.PI / 4);
			dif = MathEx.Mod(dif, 2 * Math.PI);
			if (dif > Math.PI)
				dif = 2 * Math.PI - dif;
			return dif;
		}

		const int NormalMinLineLength = 10;

		static bool PickMerge(ref Section s0, Section s1, ref Section s2)
		{
			if (s0 == null) {
				if (s2 == null)
					return false;
				else {
					s2 = Merged(s1, s2);
					return true;
				}
			} else if (s2 == null) {
				s0 = Merged(s0, s1);
				return true;
			}
			// decide the best way to merge
			double e0Before = AngleError(s0.Vector(), s0.AngleMod8), e0After = AngleError(s1.EndPt.Sub(s0.StartPt), s0.AngleMod8);
			double e2Before = AngleError(s2.Vector(), s2.AngleMod8), e2After = AngleError(s2.EndPt.Sub(s1.StartPt), s2.AngleMod8);
			if (e0Before - e0After > e2Before - e2After) {
				s0 = Merged(s0, s1);
				return true;
			} else {
				s2 = Merged(s1, s2);
				return true;
			}
		}
		static Section Merged(Section s1, Section s2)
		{
			return new Section {
				StartPt = s1.StartPt, EndPt = s2.EndPt,
				iStart = s1.iStart, iEnd = s2.iEnd,
				Length = s1.Length + s2.Length,
				AngleMod8 = AngleMod8(s2.EndPt.Sub(s1.StartPt))
			};
		}

		#endregion

		#region Mouse input handling - RecognizeScribbleForEraseOrCancel

		// To recognize a scribble we require the simplified line to reverse 
		// direction at least three times. There are separate criteria for
		// erasing a shape currently being drawn and for erasing existing
		// shapes.
		//
		// The key difference between an "erase scribble" and a "cancel 
		// scribble" is that an erase scribble starts out as such, while
		// a cancel scribble indicates that the user changed his mind, so
		// the line will not appear to be a scribble at the beginning. 
		// The difference is detected by timestamps. For example, the
		// following diagram represents an "erase" operation and a "cancel"
		// operation. Assume the input points are evenly spaced in time,
		// and that the dots represent points where the input reversed 
		// direction. 
		//
		// Input points         ..........................
		// Reversals (erase)      .  .  .  .     .     .  
		// Reversals (cancel)              .   .   .   .  
		//
		// So, a scribble is considered an erasure if it satisfies t0 < t1, 
		// where t0 is the time between mouse-down and the first reversal, 
		// and t1 is the time between the first and third reversals. A cancel
		// operation satisfies t0 > t1 instead.
		//
		// Both kinds of scribble need to satisfy the formula LL*c > CHA, 
		// where c is a constant factor in pixels, LL is the drawn line 
		// length and CHA is the area of the Convex Hull that outlines the 
		// drawn figure. This formula basically detects that the user 
		// is convering the same ground repeatedly; if the pen reverses
		// direction repeatedly but goes to new places each time, it's not
		// considered an erasure scribble. For a cancel scribble, LL is
		// computed starting from the first reversal.
		IEnumerable<Shape> RecognizeScribbleForEraseOrCancel(DragState state, out bool cancel, out List<PointT> simplified_)
		{
			cancel = false;
			var simplified = simplified_ = LineMath.SimplifyPolyline(
				state.UnfilteredPoints.Select(p => p.Point), 10);
			List<int> reversals = FindReversals(simplified, 3);
			if (reversals.Count >= 3)
			{
				// 3 reversals confirmed. Now decide: erase or cancel?
				int[] timeStampsMs = FindTimeStamps(state.UnfilteredPoints, simplified);
				int t0 = timeStampsMs[reversals[0]], t1 = timeStampsMs[reversals[2]] - t0;
				cancel = t0 > t1 + 500;

				// Now test the formula LL*c > CHA as explained above
				IListSource<PointT> simplified__ = cancel ? simplified.Slice(reversals[0]) : simplified.AsListSource();
				float LL = simplified__.AdjacentPairs().Sum(pair => pair.A.Sub(pair.B).Length());
				var hull = PointMath.ComputeConvexHull(simplified_);
				float CHA = PolygonMath.PolygonArea(hull);
				if (LL * EraseNubWidth > CHA)
				{
					// Erasure confirmed.
					if (cancel)
						return EmptyList<Shape>.Value;
					
					// Figure out which shapes to erase. To do this, we compute for 
					// each shape the amount of the scribble that overlaps that shape.
					return _doc.Shapes.Where(s => ShouldErase(s, simplified)).ToList();
				}
			}
			return null;
		}

		IEnumerable<LineSegmentT> AsLineSegments(IEnumerable<PointT> points)
		{
			return points.AdjacentPairs().Select(p => new LineSegmentT(p.A, p.B));
		}

		private bool ShouldErase(Shape s, List<PointT> mouseInput)
		{
			var mouseBBox = mouseInput.ToBoundingBox();
			var line = s as LineOrArrow;
			if (line != null)
			{
				// Count the number of crossings
				int crossings = 0;
				float lineLen = 0;
				if (line.BBox.Overlaps(mouseBBox))
					foreach(var seg in AsLineSegments(line.Points)) {
						lineLen += seg.Length();
						if (seg.ToBoundingBox().Overlaps(mouseBBox))
							crossings += FindIntersectionsWith(seg, mouseInput, false).Count();
					}
				if (crossings * 40.0f > lineLen)
					return true;
			}
			else
			{
				// Measure how much of the mouse input is inside the bbox
				var bbox = s.BBox;
				if (bbox != null) {
					var amtInside = mouseInput.AdjacentPairs()
						.Select(seg => seg.A.To(seg.B).ClipTo(bbox))
						.WhereNotNull()
						.Sum(seg => seg.Length());
					if (amtInside * EraseBoxThreshold > bbox.Area())
						return true;
				}
			}
			return false;
		}

		float EraseNubWidth = 25, EraseBoxThreshold = 40;

		List<int> FindReversals(List<PointT> points, int stopAfter)
		{
			var reversals = new List<int>();
			for (int i = 1, c = points.Count; i < c - 1; i++)
			{
				PointT p0 = points[i - 1], p1 = points[i], p2 = points[i + 1];
				VectorT v1 = p1.Sub(p0), v2 = p2.Sub(p1);
				if (v1.Dot(v2) < 0 && MathEx.IsInRange(
					MathEx.Mod(v1.AngleDeg() - v2.AngleDeg(), 360), 150, 210))
				{
					reversals.Add(i);
					if (reversals.Count >= stopAfter)
						break;
				}
			}
			return reversals;
		}
		private static int[] FindTimeStamps(DList<DragPoint> original, List<PointT> simplified)
		{
			int o = -1, timeMs = 0;
			int[] times = new int[simplified.Count];
			for (int s = 0; s < simplified.Count; s++)
			{
				var p = simplified[s];
				do {
					o++;
					timeMs += original[o].MsecSincePrev;
				} while (original[o].Point != p);
				times[s] = timeMs;
			}
			return times;
		}

		#endregion

		DiagramDocument _doc;
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public DiagramDocument Document
		{
			get { return _doc; }
			set {
				if (_doc != value) {
					if (_doc != null) {
						_doc.AfterAction -= AfterAction;
						_doc.AfterShapesAdded -= AfterShapesAdded;
						_doc.AfterShapesRemoved -= AfterShapesRemoved;
					}
					_doc = value;
					_doc.AfterAction += AfterAction;
					_doc.AfterShapesAdded += AfterShapesAdded;
					_doc.AfterShapesRemoved += AfterShapesRemoved;
				}
			}
		}

		public void Save(string filename)
		{
			using (var stream = File.Open(filename, FileMode.Create)) {
				_doc.Save(stream);
			}
		}

		public void Load(string filename)
		{
			using (var stream = File.OpenRead(filename)) {
				Document = DiagramDocument.Load(stream);
				AfterAction(true);
			}
		}

		void AfterShapesAdded(IReadOnlyCollection<Shape> newShapes)
		{
			_selectedShapes.Clear();
			_selectedShapes.Add(_partialSelShape);
			if (newShapes.Count == 1) {
				var s = newShapes.First();
				_partialSelShape = s;
				_focusShape = s;
			}
		}

		void AfterAction(bool @do)
		{
			ShapesChanged();
			RecreateSelectionAdorners();
		}

		protected void ShapesChanged()
		{
			_mainLayer.Shapes.Clear();
			foreach (Shape shape in _doc.Shapes)
				shape.AddLLShapesTo(_mainLayer.Shapes);
			_mainLayer.Invalidate();
		}

		const int AnchorSnapDistance = 10;

		public Anchor GetBestAnchor(PointT input, int exitAngleMod8 = -1)
		{
			var candidates =
				from shape in _doc.Shapes
				let anchor = shape.GetNearestAnchor(input, exitAngleMod8)
				where anchor != null && anchor.Point.Sub(input).Quadrance() <= MathEx.Square(AnchorSnapDistance)
				select anchor;
			return candidates.MinOrDefault(a => a.Point.Sub(input).Quadrance());
		}

		[Command(null, "Select all shapes")] public bool SelectAll(bool run = true)
		{
			if (run) {
				_selectedShapes.AddRange(_doc.Shapes);
				RecreateSelectionAdorners();
			}
			return _doc.Shapes.Count != 0;
		}
	}


	public interface IRecognizerResult
	{
		IEnumerable<LLShape> RealtimeDisplay { get; }
		int Quality { get; }
		void Accept();
	}

}