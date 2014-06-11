using System;
using MonoMac.AppKit;
using Eto.Forms;
using System.Collections.Generic;
using MonoMac.Foundation;
using Eto.Drawing;
using sd = System.Drawing;
using System.Collections;
using MonoMac.ObjCRuntime;
using System.Linq;

namespace Eto.Mac.Forms.Controls
{
	public class GridViewHandler : GridHandler<NSTableView, GridView, GridView.ICallback>, GridView.IHandler
	{
		CollectionHandler collection;

		public class EtoTableView : NSTableView, IMacControl
		{
			public WeakReference WeakHandler { get; set; }

			public GridViewHandler Handler
			{ 
				get { return (GridViewHandler)WeakHandler.Target; }
				set { WeakHandler = new WeakReference(value); } 
			}

			/// <summary>
			/// The area to the right and below the rows is not filled with the background
			/// color. This fixes that. See http://orangejuiceliberationfront.com/themeing-nstableview/
			/// </summary>
			public override void DrawBackground(sd.RectangleF clipRect)
			{
				var backgroundColor = Handler.BackgroundColor;
				if (backgroundColor != Colors.Transparent)
				{
					backgroundColor.ToNSUI().Set();
					NSGraphics.RectFill(clipRect);
				}
				else
					base.DrawBackground(clipRect);
			}
		}

		class EtoTableViewDataSource : NSTableViewDataSource
		{
			WeakReference handler;

			public GridViewHandler Handler { get { return (GridViewHandler)(handler != null ? handler.Target : null); } set { handler = new WeakReference(value); } }

			public override int GetRowCount(NSTableView tableView)
			{
				return (Handler.collection != null && Handler.collection.Collection != null) ? Handler.collection.Count : 0;
			}

			public override NSObject GetObjectValue(NSTableView tableView, NSTableColumn tableColumn, int row)
			{
				var item = Handler.collection.ElementAt(row);
				var colHandler = Handler.GetColumn(tableColumn);
				return colHandler == null ? null : colHandler.GetObjectValue(item);
			}

			public override void SetObjectValue(NSTableView tableView, NSObject theObject, NSTableColumn tableColumn, int row)
			{
				var item = Handler.collection.ElementAt(row);
				var colHandler = Handler.GetColumn(tableColumn);
				if (colHandler != null)
				{
					colHandler.SetObjectValue(item, theObject);

					Handler.Callback.OnCellEdited(Handler.Widget, new GridViewCellEventArgs(colHandler.Widget, row, colHandler.Column, item));
				}
			}
		}

		class EtoTableDelegate : NSTableViewDelegate
		{
			WeakReference handler;

			public GridViewHandler Handler { get { return (GridViewHandler)(handler != null ? handler.Target : null); } set { handler = new WeakReference(value); } }

			public override bool ShouldEditTableColumn(NSTableView tableView, NSTableColumn tableColumn, int row)
			{
				var colHandler = Handler.GetColumn(tableColumn);
				var item = Handler.collection.ElementAt(row);
				var args = new GridViewCellEventArgs(colHandler.Widget, row, colHandler.Column, item);
				Handler.Callback.OnCellEditing(Handler.Widget, args);
				return true;
			}

			public override void SelectionDidChange(NSNotification notification)
			{
				if (Handler.SuppressSelectionChanged == 0)
				{
					Handler.Callback.OnSelectionChanged(Handler.Widget, EventArgs.Empty);
				}
			}

			public override void DidClickTableColumn(NSTableView tableView, NSTableColumn tableColumn)
			{
				var colHandler = Handler.GetColumn(tableColumn);
				Handler.Callback.OnColumnHeaderClick(Handler.Widget, new GridColumnEventArgs(colHandler.Widget));
			}

			public override void WillDisplayCell(NSTableView tableView, NSObject cell, NSTableColumn tableColumn, int row)
			{
				var colHandler = Handler.GetColumn(tableColumn);
				var item = Handler.GetItem(row);
				Handler.OnCellFormatting(colHandler.Widget, item, row, cell as NSCell);

			}

			public override void ColumnDidResize(NSNotification notification)
			{
				if (!Handler.IsAutoSizingColumns)
				{
					// when the user resizes the column, don't autosize anymore when data/scroll changes
					var column = notification.UserInfo["NSTableColumn"] as NSTableColumn;
					if (column != null)
					{
						var colHandler = Handler.GetColumn(column);
						colHandler.AutoSize = false;
					}
				}
			}
		}

		public bool ShowCellBorders
		{
			get { return Control.IntercellSpacing.Width > 0 || Control.IntercellSpacing.Height > 0; }
			set { Control.IntercellSpacing = value ? new sd.SizeF(1, 1) : sd.SizeF.Empty; } 
		}

		public override void AttachEvent(string id)
		{
			switch (id)
			{
				case Grid.CellEditingEvent:
				// handled by delegate
				/* following should work, but internal delegate to trigger event does not work
				table.ShouldEditTableColumn = (tableView, tableColumn, row) => {
					var id = tableColumn.Identifier as EtoGridColumnIdentifier;
					var item = store.GetItem (row);
					var args = new GridViewCellArgs(id.Handler.Widget, row, id.Handler.Column, item);
					this.Widget.OnBeginCellEdit (args);
					return true;
				};*/
					break;
				case Grid.CellEditedEvent:
				// handled after object value is set
					break;
				case Grid.SelectionChangedEvent:
				/* handled by delegate, for now
				table.SelectionDidChange += delegate {
					Widget.OnSelectionChanged (EventArgs.Empty);
				};*/
					break;
				case Grid.ColumnHeaderClickEvent:
				/*
				table.DidClickTableColumn += delegate(object sender, NSTableViewTableEventArgs e) {
					var column = Handler.Widget.Columns.First (r => object.ReferenceEquals (r.ControlObject, tableColumn));
					Handler.Widget.OnHeaderClick (new GridColumnEventArgs (column));
				};
				*/
					break;
				case Grid.CellFormattingEvent:
					break;
				default:
					base.AttachEvent(id);
					break;
			}
		}

		public GridViewHandler()
		{
			Control = new EtoTableView
			{
				Handler = this,
				FocusRingType = NSFocusRingType.None,
				DataSource = new EtoTableViewDataSource { Handler = this },
				Delegate = new EtoTableDelegate { Handler = this },
				ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.None
			};
		}

		public IEnumerable<object> SelectedItems
		{
			get
			{ 
				if (collection != null)
				{
					foreach (var row in SelectedRows)
						yield return collection.ElementAt(row);
				}
			}
		}

		class CollectionHandler : EnumerableChangedHandler<object>
		{
			public GridViewHandler Handler { get; set; }

			public override void AddRange(IEnumerable<object> items)
			{
				Handler.Control.ReloadData();
				Handler.AutoSizeColumns();
			}

			static Selector selInsertRowsWithAnimation = new Selector("insertRowsAtIndexes:withAnimation:");
			static Selector selRemoveRowsWithAnimation = new Selector("removeRowsAtIndexes:withAnimation:");

			public override void AddItem(object item)
			{
				if (Handler.Control.RespondsToSelector(selInsertRowsWithAnimation))
				{
					Handler.Control.BeginUpdates();
					Handler.Control.InsertRows(new NSIndexSet(Count), NSTableViewAnimation.SlideDown);
					Handler.Control.EndUpdates();
				}
				else
					Handler.Control.ReloadData();

				Handler.AutoSizeColumns();
			}

			public override void InsertItem(int index, object item)
			{
				if (Handler.Control.RespondsToSelector(selInsertRowsWithAnimation))
				{
					Handler.Control.BeginUpdates();
					Handler.Control.InsertRows(new NSIndexSet(index), NSTableViewAnimation.SlideDown);
					Handler.Control.EndUpdates();
				}
				else
				{
					var rows = Handler.SelectedRows.Select(r => r >= index ? r + 1 : r).ToArray();
					Handler.SuppressSelectionChanged++;
					Handler.Control.ReloadData();
					Handler.SelectedRows = rows;
					Handler.SuppressSelectionChanged--;
				}

				Handler.AutoSizeColumns();
			}

			public override void RemoveItem(int index)
			{
				if (Handler.Control.RespondsToSelector(selRemoveRowsWithAnimation))
				{
					Handler.Control.BeginUpdates();
					Handler.Control.RemoveRows(new NSIndexSet(index), NSTableViewAnimation.SlideUp);
					Handler.Control.EndUpdates();
				}
				else
				{
					// need to adjust selected rows to shift them up
					bool isSelected = false;
					var rows = Handler.SelectedRows.Where(r =>
					{
						if (r != index)
							return true;
						isSelected = true;
						return false;
					}).Select(r => r > index ? r - 1 : r).ToArray();
					Handler.SuppressSelectionChanged++;
					Handler.Control.ReloadData();
					Handler.SelectedRows = rows;
					Handler.SuppressSelectionChanged--;
					// item being removed was selected, so trigger change
					if (isSelected)
						Handler.Callback.OnSelectionChanged(Handler.Widget, EventArgs.Empty);
				}

				Handler.AutoSizeColumns();
			}

			public override void RemoveAllItems()
			{
				Handler.Control.ReloadData();
				Handler.AutoSizeColumns();
			}
		}

		public IEnumerable<object> DataStore
		{
			get { return collection != null ? collection.Collection : null; }
			set
			{
				if (collection != null)
					collection.Unregister();
				collection = new CollectionHandler{ Handler = this };
				collection.Register(value);
				if (Widget.Loaded)
					AutoSizeColumns();
			}
		}

		public override object GetItem(int row)
		{
			return collection.ElementAt(row);
		}
	}
}
