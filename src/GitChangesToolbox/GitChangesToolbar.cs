using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace GitChangesToolbox
{
    /// <summary>
    /// 在 Git 更改窗口顶部工具栏注入折叠和定位按钮。
    /// </summary>
    internal sealed class GitChangesToolbar
    {
        private const string PanelName = "buttonPanel";
        private const string CommitButtonName = "commitButton";
        private const string CommitToAllButtonName = "commitToAllButton";
        private const string WrapperTag = "GitChangesToolboxCommitWrapper";
        private const string CommitAllTextChinese = "全部提交";
        private const string CommitAllTextEnglish = "Commit All";
        private const string CollapseTag = "GitChangesToolboxCollapseButton";
        private const string LocateTag = "GitChangesToolboxLocateButton";

        private readonly IServiceProvider _serviceProvider;
        private DispatcherTimer _timer;
        private Button _collapseButton;
        private CrispImage _collapseIcon;

        public GitChangesToolbar(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Start()
        {
            EnsureButtons();

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromSeconds(3);
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            EnsureButtons();
        }

        private void EnsureButtons()
        {
            try
            {
                DependencyObject root = GitChangesController.FindGitWindowRoot(_serviceProvider);
                if (root == null)
                    return;

                Button collapseButton = FindTaggedButton(root, CollapseTag);
                Button locateButton = FindTaggedButton(root, LocateTag);
                if (collapseButton == null || locateButton == null)
                {
                    DetachFromParent(collapseButton);
                    DetachFromParent(locateButton);

                    collapseButton = CreateIconButton(
                        new CrispImage { Moniker = KnownMonikers.CollapseAll },
                        "折叠更改列表中的所有文件夹",
                        CollapseTag,
                        OnCollapseClick);
                    locateButton = CreateIconButton(
                        new CrispImage { Moniker = KnownMonikers.LocateControl },
                        "定位到当前打开的文件并选中",
                        LocateTag,
                        OnLocateClick);
                }

                _collapseButton = collapseButton;
                _collapseIcon = collapseButton.Content as CrispImage;

                Button commitButton = FindCommitButton(root);
                if (commitButton != null)
                {
                    if (IsBeforeCommitButton(commitButton, collapseButton, locateButton))
                        return;

                    WrapBeforeCommitButton(commitButton, collapseButton, locateButton);
                    return;
                }

                StackPanel panel = FindByName(root, PanelName) as StackPanel;
                if (panel == null)
                    return;

                if (IsAtPanelFront(panel, collapseButton, locateButton))
                    return;

                DetachFromParent(collapseButton);
                DetachFromParent(locateButton);
                panel.Children.Insert(0, locateButton);
                panel.Children.Insert(0, collapseButton);
            }
            catch (Exception)
            {
            }
        }

        private Button CreateIconButton(CrispImage icon, string toolTip, string tag, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = icon,
                Tag = tag,
                ToolTip = toolTip,
                Style = CreateToolbarButtonStyle(),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false
            };
            button.Click += handler;

            return button;
        }

        private static Style CreateToolbarButtonStyle()
        {
            Style style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(3)));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 24.0));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 24.0));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowButtonHoverActiveBrushKey)));
            style.Triggers.Add(hover);

            Trigger pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(EnvironmentColors.ToolWindowButtonDownBrushKey)));
            style.Triggers.Add(pressed);

            return style;
        }

        private void OnCollapseClick(object sender, RoutedEventArgs e)
        {
            bool hasExpanded = GitChangesController.HasExpandedNodes(_serviceProvider);
            if (hasExpanded)
            {
                int collapsed = GitChangesController.CollapseAll(_serviceProvider);
                UpdateCollapseButton(true);
                ShowStatus(collapsed > 0 ? $"已折叠 {collapsed} 项" : "没有可折叠的项");
                return;
            }

            int expanded = GitChangesController.ExpandAll(_serviceProvider);
            UpdateCollapseButton(false);
            ShowStatus(expanded > 0 ? $"已展开 {expanded} 项" : "没有可展开的项");
        }

        private void UpdateCollapseButton(bool collapsed)
        {
            if (_collapseIcon != null)
                _collapseIcon.Moniker = collapsed ? KnownMonikers.ExpandAll : KnownMonikers.CollapseAll;

            if (_collapseButton != null)
                _collapseButton.ToolTip = collapsed ? "展开更改列表中的所有文件夹" : "折叠更改列表中的所有文件夹";
        }

        private void OnLocateClick(object sender, RoutedEventArgs e)
        {
            string filePath = GitChangesController.GetActiveDocumentPath(_serviceProvider);
            if (string.IsNullOrEmpty(filePath))
            {
                ShowStatus("当前没有打开的文件");
                return;
            }

            bool located = GitChangesController.Locate(_serviceProvider, filePath);
            ShowStatus(located ? "已定位到当前文件" : "当前文件不在更改列表中");
        }

        private void ShowStatus(string text)
        {
            try
            {
                IVsStatusbar statusBar = ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;
                if (statusBar != null)
                    statusBar.SetText(text);
            }
            catch (Exception)
            {
            }
        }

        private static Button FindTaggedButton(DependencyObject element, string tag)
        {
            if (element == null)
                return null;

            Button button = element as Button;
            if (button != null && string.Equals(button.Tag as string, tag, StringComparison.Ordinal))
                return button;

            if (!(element is Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
                return null;

            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                Button found = FindTaggedButton(VisualTreeHelper.GetChild(element, i), tag);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Button FindCommitButton(DependencyObject element)
        {
            if (element == null)
                return null;

            Button button = element as Button;
            if (button != null && button.IsVisible && IsCommitButton(button))
                return button;

            if (!(element is Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
                return null;

            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                Button found = FindCommitButton(VisualTreeHelper.GetChild(element, i));
                if (found != null)
                    return found;
            }

            return null;
        }

        private static bool IsCommitButton(Button button)
        {
            if (string.Equals(button.Name, CommitButtonName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(button.Name, CommitToAllButtonName, StringComparison.OrdinalIgnoreCase))
                return true;

            return IsCommitAllText(CollectText(button));
        }

        private static bool IsCommitAllText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.IndexOf(CommitAllTextChinese, StringComparison.Ordinal) >= 0
                || text.IndexOf(CommitAllTextEnglish, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string CollectText(DependencyObject element)
        {
            StringBuilder builder = new StringBuilder();
            CollectText(element, builder);

            return builder.ToString();
        }

        private static void CollectText(DependencyObject element, StringBuilder builder)
        {
            if (element == null)
                return;

            TextBlock textBlock = element as TextBlock;
            if (textBlock != null)
                builder.Append(textBlock.Text);

            if (!(element is Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
                return;

            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
                CollectText(VisualTreeHelper.GetChild(element, i), builder);
        }

        private static bool IsBeforeCommitButton(Button commitButton, Button collapseButton, Button locateButton)
        {
            StackPanel wrapper = commitButton.Parent as StackPanel;
            if (wrapper == null || !string.Equals(wrapper.Tag as string, WrapperTag, StringComparison.Ordinal))
                return false;

            if (wrapper.Children.Count < 3)
                return false;

            return ReferenceEquals(wrapper.Children[0], collapseButton)
                && ReferenceEquals(wrapper.Children[1], locateButton);
        }

        private static void WrapBeforeCommitButton(Button commitButton, Button collapseButton, Button locateButton)
        {
            Panel parent = VisualTreeHelper.GetParent(commitButton) as Panel;
            if (parent == null)
                return;

            StackPanel wrapper = parent as StackPanel;
            if (wrapper == null || !string.Equals(wrapper.Tag as string, WrapperTag, StringComparison.Ordinal))
            {
                int index = parent.Children.IndexOf(commitButton);
                if (index < 0)
                    return;

                wrapper = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Tag = WrapperTag,
                    VerticalAlignment = commitButton.VerticalAlignment
                };
                CopyLayoutProperties(commitButton, wrapper);

                parent.Children.RemoveAt(index);
                wrapper.Children.Add(commitButton);
                parent.Children.Insert(index, wrapper);
            }

            DetachFromParent(collapseButton);
            DetachFromParent(locateButton);
            wrapper.Children.Insert(0, collapseButton);
            wrapper.Children.Insert(1, locateButton);
        }

        private static bool IsAtPanelFront(StackPanel panel, Button collapseButton, Button locateButton)
        {
            if (panel.Children.Count < 2)
                return false;

            return ReferenceEquals(panel.Children[0], collapseButton)
                && ReferenceEquals(panel.Children[1], locateButton);
        }

        private static void DetachFromParent(Button button)
        {
            if (button == null)
                return;

            Panel panel = VisualTreeHelper.GetParent(button) as Panel;
            if (panel != null)
                panel.Children.Remove(button);
        }

        private static void CopyLayoutProperties(FrameworkElement source, FrameworkElement target)
        {
            target.HorizontalAlignment = source.HorizontalAlignment;
            target.VerticalAlignment = source.VerticalAlignment;

            target.SetValue(Grid.RowProperty, source.GetValue(Grid.RowProperty));
            target.SetValue(Grid.ColumnProperty, source.GetValue(Grid.ColumnProperty));
            target.SetValue(Grid.RowSpanProperty, source.GetValue(Grid.RowSpanProperty));
            target.SetValue(Grid.ColumnSpanProperty, source.GetValue(Grid.ColumnSpanProperty));
            target.SetValue(DockPanel.DockProperty, source.GetValue(DockPanel.DockProperty));
            target.SetValue(Canvas.LeftProperty, source.GetValue(Canvas.LeftProperty));
            target.SetValue(Canvas.TopProperty, source.GetValue(Canvas.TopProperty));
            target.SetValue(Canvas.RightProperty, source.GetValue(Canvas.RightProperty));
            target.SetValue(Canvas.BottomProperty, source.GetValue(Canvas.BottomProperty));
            target.SetValue(Panel.ZIndexProperty, source.GetValue(Panel.ZIndexProperty));
        }

        private static FrameworkElement FindByName(DependencyObject element, string name)
        {
            if (element == null)
                return null;

            FrameworkElement frameworkElement = element as FrameworkElement;
            if (frameworkElement != null && string.Equals(frameworkElement.Name, name, StringComparison.Ordinal))
                return frameworkElement;

            if (!(element is System.Windows.Media.Visual) && !(element is System.Windows.Media.Media3D.Visual3D))
                return null;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                FrameworkElement found = FindByName(System.Windows.Media.VisualTreeHelper.GetChild(element, i), name);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}