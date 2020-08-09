using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using VSLangProj80;

namespace FsWorksheet
{
    internal sealed class WorksheetSpace
    {
        /// <summary>
        /// Text view to add the adornment on.
        /// </summary>
        private readonly IWpfTextView view;

        /// <summary>
        /// Adornment image
        /// </summary>        

        /// <summary>
        /// The layer for the adornment.
        /// </summary>
        private readonly IAdornmentLayer adornmentLayer;

        private UIElement Element;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorksheetSpace"/> class.
        /// Creates a square image and attaches an event handler to the layout changed event that
        /// adds the the square in the upper right-hand corner of the TextView via the adornment layer
        /// </summary>
        /// <param name="view">The <see cref="IWpfTextView"/> upon which the adornment will be drawn</param>
        public WorksheetSpace(IWpfTextView view)
        {

            this.adornmentLayer = view.GetAdornmentLayer("WorksheetBar");
            this.view.LayoutChanged += OnViewLayoutChanged;
            this.view.ZoomLevelChanged += OnSizeChanged;

            var brush = ToBrush(EnvironmentColors.BrandedUITextColorKey);
            //this.Worksheet = new Worksheet()
        }

        private void OnViewLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if(e.HorizontalTranslation || e.VerticalTranslation)
            {
                Trace.WriteLine("Scrolled");
                Trace.WriteLine($"{LogicalTreeHelper.GetParent(this.Element)}");
            }    
            
        }

        private static SolidColorBrush ToBrush(ThemeResourceKey key)
        {
            var color = VSColorTheme.GetThemedColor(key);
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        }

        /// <summary>
        /// Event handler for viewport layout changed event. Adds adornment at the top right corner of the viewport.
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        private void OnSizeChanged(object sender, EventArgs e)
        {
            
            // Clear the adornment layer of previous adornments
            this.adornmentLayer.RemoveAllAdornments();
            // Place the image in the top right hand corner of the Viewport
            //Canvas.SetLeft(this.image, this.view.ViewportRight - RightMargin - AdornmentWidth);
            //Canvas.SetTop(this.image, this.view.ViewportTop + TopMargin);
            Trace.WriteLine($"{view.ViewportTop/view.LineHeight} -> {view.ViewportBottom/ view.LineHeight}");
            Canvas.SetTop(this.Element, view.LineHeight * 20);
            

            // Add the image to the adornment layer and make it relative to the viewport
            this.adornmentLayer.AddAdornment(this.view.TextSnapshot.GetLineFromLineNumber(12).Extent, null, this.Element);
        }
    }
}
