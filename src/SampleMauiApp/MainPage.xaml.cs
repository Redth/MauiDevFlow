namespace SampleMauiApp;

public partial class MainPage : ContentPage
{
    private readonly TodoService _todoService;

    public MainPage(TodoService todoService)
    {
        InitializeComponent();
        _todoService = todoService;
        SetupTodoList();
        UpdateCount();
        _todoService.Changed += OnTodoServiceChanged;
    }

    private void OnTodoServiceChanged()
    {
        Dispatcher.Dispatch(UpdateCount);
    }

    private void SetupTodoList()
    {
        TodoList.ItemsSource = _todoService.Items;
        TodoList.ItemTemplate = new DataTemplate(() =>
        {
            var border = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4)
            };
            border.SetAppThemeColor(Border.BackgroundColorProperty,
                Color.FromArgb("#FFFFFF"), Color.FromArgb("#2B2930"));
            border.SetAppThemeColor(Border.StrokeProperty,
                Color.FromArgb("#E0E0E0"), Color.FromArgb("#49454F"));

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                ColumnSpacing = 12
            };

            var checkBox = new CheckBox
            {
                Color = Color.FromArgb("#6750A4"),
                AutomationId = "TodoCheckBox"
            };
            checkBox.SetBinding(CheckBox.IsCheckedProperty, "IsCompleted");
            checkBox.CheckedChanged += OnTodoCheckedChanged;

            var label = new Label
            {
                FontSize = 16,
                VerticalOptions = LayoutOptions.Center,
                AutomationId = "TodoTitle"
            };
            label.SetBinding(Label.TextProperty, "Title");
            label.SetAppThemeColor(Label.TextColorProperty,
                Color.FromArgb("#1C1B1F"), Color.FromArgb("#E6E1E5"));

            var descLabel = new Label
            {
                FontSize = 12,
                AutomationId = "TodoDescription"
            };
            descLabel.SetBinding(Label.TextProperty, "Description");
            descLabel.SetBinding(Label.IsVisibleProperty, new Binding("Description", converter: new StringNotEmptyConverter()));
            descLabel.SetAppThemeColor(Label.TextColorProperty,
                Color.FromArgb("#49454F"), Color.FromArgb("#CAC4D0"));

            var deleteButton = new Button
            {
                Text = "✕",
                BackgroundColor = Colors.Transparent,
                TextColor = Color.FromArgb("#B3261E"),
                FontSize = 18,
                Padding = new Thickness(8, 0),
                AutomationId = "DeleteButton"
            };
            deleteButton.Clicked += OnDeleteTodo;

            Grid.SetColumn(checkBox, 0);
            Grid.SetRowSpan(checkBox, 2);
            Grid.SetColumn(label, 1);
            Grid.SetColumn(descLabel, 1);
            Grid.SetRow(descLabel, 1);
            Grid.SetColumn(deleteButton, 2);
            Grid.SetRowSpan(deleteButton, 2);

            grid.Children.Add(checkBox);
            grid.Children.Add(label);
            grid.Children.Add(descLabel);
            grid.Children.Add(deleteButton);

            border.Content = grid;
            return border;
        });
    }

    private async void OnShowModal(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new ModalTestPage());
    }

    private void OnAddTodo(object? sender, EventArgs e)
    {
        var text = NewTodoEntry.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var desc = NewDescriptionEntry.Text?.Trim() ?? "";
        _todoService.Add(text, desc);
        NewTodoEntry.Text = string.Empty;
        NewDescriptionEntry.Text = string.Empty;
        NewTodoEntry.Unfocus();
    }

    private void OnDeleteTodo(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.BindingContext is TodoItem item)
            _todoService.Remove(item);
    }

    private void OnTodoCheckedChanged(object? sender, CheckedChangedEventArgs e)
    {
        _todoService.NotifyChanged();
    }

    private void UpdateCount()
    {
        CountLabel.Text = _todoService.Summary;
    }
}

public class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
