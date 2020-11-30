using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using VSLangProj80;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;
using Runs = System.Tuple<System.ConsoleColor, string>;
using VsBrushes = Microsoft.VisualStudio.Shell.VsBrushes;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Documents;
using System.IO;

namespace FsWorksheet
{
    public class CellView : IEquatable<CellView>
    {
        public CellView(vscell cell, ITextSnapshot snapshot)
        {
            this.EqHash = cell.eqHash;
            this.Span = RangeToTrackingSpan(cell.range, snapshot);
        }

        public int EqHash { get; }

        public ITrackingSpan Span { get; set; }

        public Border Element { get; } = new Border()
        {
            Background = Brushes.White,
            BorderBrush = Brushes.LightBlue,
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        public double Top => Canvas.GetTop(Element);

        public double Left => Canvas.GetLeft(Element);

        public double Bottom => Top + Element.Height;

        public override bool Equals(object obj) => Equals(obj as CellView);

        public bool Equals(CellView other) => EqHash == other?.EqHash;

        public override int GetHashCode() => EqHash;

        public static ITrackingSpan RangeToTrackingSpan(vsrange vsrange, ITextSnapshot textSnapshot)
        {
            var from = vsrange.From;
            ITextSnapshotLine anchor = textSnapshot.GetLineFromLineNumber(from.Line);
            var start = anchor.Start.Position;
            var to = vsrange.To;
            var end = textSnapshot.GetLineFromLineNumber(to.Line).Start.Position + to.Col;
            return textSnapshot.CreateTrackingSpan(start, end - start, SpanTrackingMode.EdgeInclusive);
        }

        public void MoveTo(vsrange range, ITextSnapshot currentSnapshot)
        {
            Span = RangeToTrackingSpan(range, currentSnapshot);
        }

        public SnapshotSpan TranslateTo(IWpfTextView view)
        {
            var span = this.Span.GetSpan(view.TextSnapshot);
            var g = view.TextViewLines.GetMarkerGeometry(span);

            if (g == null)
                return span;

            Element.Width = view.ViewportWidth * 0.33;
            Element.MinHeight = view.LineHeight;
            Canvas.SetLeft(Element, view.ViewportWidth - Element.Width);
            Canvas.SetTop(Element, g.Bounds.Top);
            Panel.SetZIndex(Element, span.Start.Position);
            return span;
        }
    }

    public sealed class WorksheetSpace : IDisposable
    {
        public const string AdornerName = "FsWorksheet";

        public IWpfTextView TextView { get; }

        private readonly IAdornmentLayer adornmentLayer;
        private readonly ITextDocument document;
        private WorksheetModel Client;
        private Dictionary<vscell, CellView> Cells = new Dictionary<vscell, CellView>();

        public ProgressBar ProgressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 3,
            BorderThickness = new Thickness(),
            Visibility = Visibility.Collapsed,
            Background = ToBrush(ProgressBarColors.BackgroundBrushKey)
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="WorksheetSpace"/> class.
        /// Creates a square image and attaches an event handler to the layout changed event that
        /// adds the the square in the upper right-hand corner of the TextView via the adornment layer
        /// </summary>
        /// <param name="textView">The <see cref="IWpfTextView"/> upon which the adornment will be drawn</param>
        public WorksheetSpace(IWpfTextView textView)
        {
            if (textView.IsEmbeddedTextView())
                return;

            var document = textView.TextBuffer.Properties.GetProperty<ITextDocument>(typeof(ITextDocument));
            if (Path.GetExtension(document.FilePath) != ".fsx")
                return;

            this.TextView = textView;
            this.document = document;
            this.adornmentLayer = textView.GetAdornmentLayer(AdornerName);

            this.TextView.LayoutChanged += OnViewLayoutChanged;
            this.TextView.ViewportWidthChanged += OnViewportResized;
            this.document.FileActionOccurred += OnFileAction;
            this.TextView.TextBuffer.ChangedLowPriority += OnBufferChanged;
            this.TextView.Closed += OnClosed;

            StartServer();

            this.IsValid = true;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            Dispose();
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            foreach (var change in e.Changes)
                foreach (var cell in Cells)
                {
                    //change.NewSpan.IntersectsWith(cell.Value.Span);
                }

        }

        private void OnViewportResized(object sender, EventArgs e)
        {
            this.adornmentLayer.RemoveAdornment(ProgressBar);
            ProgressBar.Width = TextView.ViewportWidth;
            this.adornmentLayer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, null, ProgressBar, null);

            foreach (var cells in Cells.Values.ToArray())
            {
                cells.TranslateTo(TextView);
            }
        }

        private void OnRemoved(object tag, UIElement element)
        {
            var cell = tag as vscell;
            if (cell == null || !Cells.TryGetValue(cell, out var existing))
                return;

            if (existing.Top > TextView.ViewportTop && existing.Top < TextView.ViewportBottom)
            {
                AddCell(cell, existing);
            }
        }

        private void Compute()
        {
            if (this.Client != null)
            {
                ShowProgress();
                this.Client?.Notify(WorksheetCommand.NewCompute(GetText()));
            }
        }

        private void LogTask(Task task)
        {

        }

        public void StartServer()
        {
            LogTask(Task.Run(StartServerSync));
        }

        public void StartServerSync()
        {
            var pipename = $"worksheet-{Guid.NewGuid()}";
            this.Client = new WorksheetModel(pipename, document.FilePath, ThreadModel);
            this.Client.CellChanged += OnCellChanged;
            this.Client.Start();
            this.Server = new WorksheetServer(pipename, document.FilePath);
            this.Server.Start();
        }

        private void ThreadModel(FSharpAsync<Unit> obj)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                FSharpAsync.StartImmediate(obj, null);
            });
        }

        private void OnFileAction(object sender, TextDocumentFileActionEventArgs e)
        {
            if (e.FileActionType == FileActionTypes.ContentSavedToDisk)
            {
                Compute();
            }
        }

        private string GetText()
        {
            return CurrentSnapshot.GetText();
        }

        private void OnCellChanged(object sender, (ChangeEvents, vscell, Runs[] runs) args)
        {
            var (change, cell, runs) = args;

            switch (change)
            {
                case ChangeEvents.Unchanged:
                    // do nothing
                    break;

                case ChangeEvents.Added:
                    {
                        var newcell = new CellView(cell, CurrentSnapshot);
                        Cells.Add(cell, newcell);
                        AddCell(cell, newcell);
                        break;
                    }

                case ChangeEvents.Moved:
                    {
                        if (Cells.TryGetValue(cell, out var existing))
                        {
                            existing.MoveTo(cell.range, CurrentSnapshot);
                            RemoveCell(existing);
                            AddCell(cell, existing);
                        }
                        break;
                    }

                case ChangeEvents.Removed:
                    {
                        if (Cells.TryGetValue(cell, out var existing))
                        {
                            Cells.Remove(cell);
                            RemoveCell(existing);
                        }
                        break;
                    }

                case ChangeEvents.Evaluating:
                    {
                        if (Cells.TryGetValue(cell, out var existing))
                        {
                            PaintCell(existing, Brushes.PaleGreen);
                        }
                        break;
                    }

                case ChangeEvents.Evaluated:
                    {
                        if (Cells.TryGetValue(cell, out var existing))
                        {
                            UpdateCellContents(runs, existing);
                            PaintCell(existing, Brushes.LightGray);
                        }
                        break;
                    }


                case ChangeEvents.Committed:
                    {
                        HideProgress();
                        ReAddAll();
                        break;
                    }

                default:
                    break;
            }
        }

        private void ReAddAll()
        {

            foreach (var pair in Cells)
            {
                RemoveCell(pair.Value);
                AddCell(pair.Key, pair.Value);
            }
        }

        public FontFamily Font { get; set; } = new FontFamily("Consolas");

        private void UpdateCellContents(Runs[] runs, CellView existing)
        {
            var textBlock = new TextBlock
            {
                FontFamily = Font,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = TextView.LineHeight,
            };

            foreach (var (color, text) in runs)
            {
                textBlock.Inlines.Add(new Run(text) { Foreground = ToBrush(color) });
            }

            existing.Element.Child = textBlock;
        }

        private Brush ToBrush(ConsoleColor color)
        {
            switch (color)
            {
                case ConsoleColor.Green:
                    return Brushes.DarkGreen;
                case ConsoleColor.Cyan:
                    return Brushes.DarkCyan;
                case ConsoleColor.Gray:
                    return Brushes.Gray;
                case ConsoleColor.Red:
                    return Brushes.DarkRed;
                case ConsoleColor.White:
                    return Brushes.DarkGray;
                default:
                    return Brushes.Black;
            }
        }

        private bool AddCell(vscell key, CellView existing)
        {
            var span = existing.TranslateTo(TextView);
            var added = adornmentLayer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, key, existing.Element, OnRemoved);
            return true;
        }

        private void RemoveCell(CellView existing)
        {
            adornmentLayer.RemoveAdornment(existing.Element);
        }

        private void PaintCell(CellView cell, Brush brush)
        {
            cell.Element.BorderBrush = brush;
        }

        private ITextSnapshot CurrentSnapshot => this.TextView.TextBuffer.CurrentSnapshot;

        public WorksheetServer Server { get; private set; }
        public bool IsValid { get; }

        public event EventHandler Disposed;

        public void ShowProgress()
        {
            ProgressBar.Visibility = Visibility.Visible;
        }

        public void HideProgress()
        {
            ProgressBar.Visibility = Visibility.Collapsed;
        }

        private void OnViewLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (e.HorizontalTranslation || e.VerticalTranslation)
            {
                OnScrolled();
                //Trace.WriteLine("Scrolled");
                //Trace.WriteLine($"{LogicalTreeHelper.GetParent(this.Element)}");
            }

        }

        private void OnScrolled()
        {

        }

        private static SolidColorBrush ToBrush(Microsoft.VisualStudio.Shell.ThemeResourceKey key)
        {
            var color = VSColorTheme.GetThemedColor(key);
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        public void Dispose()
        {
            if (!IsValid)
                return;


            this.TextView.LayoutChanged -= OnViewLayoutChanged;
            this.TextView.ViewportWidthChanged -= OnViewportResized;
            this.document.FileActionOccurred -= OnFileAction;
            this.TextView.TextBuffer.ChangedLowPriority -= OnBufferChanged;
            this.TextView.Closed -= OnClosed;

            Server.Dispose();
            this.Disposed?.Invoke(this, EventArgs.Empty);
        }
    }
}
