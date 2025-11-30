using FluentAssertions;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class SharedModelsTests
{
    [Fact]
    public void VisualTreeNode_CanBeCreated()
    {
        // Arrange & Act
        var node = new VisualTreeNode
        {
            Handle = "elem_123",
            TypeName = "System.Windows.Controls.Button",
            Name = "SubmitButton",
            Depth = 3,
            Children = new List<VisualTreeNode>
            {
                new VisualTreeNode
                {
                    Handle = "elem_124",
                    TypeName = "System.Windows.Controls.TextBlock",
                    Depth = 4
                }
            }
        };

        // Assert
        node.Handle.Should().Be("elem_123");
        node.TypeName.Should().Be("System.Windows.Controls.Button");
        node.Name.Should().Be("SubmitButton");
        node.Depth.Should().Be(3);
        node.Children.Should().HaveCount(1);
        node.Children[0].Handle.Should().Be("elem_124");
    }

    [Fact]
    public void VisualTreeResult_ContainsTreeMetadata()
    {
        // Arrange & Act
        var result = new VisualTreeResult
        {
            Root = new VisualTreeNode { Handle = "root" },
            TotalElements = 42,
            MaxDepthReached = true
        };

        // Assert
        result.Root.Handle.Should().Be("root");
        result.TotalElements.Should().Be(42);
        result.MaxDepthReached.Should().BeTrue();
    }

    [Fact]
    public void PropertyInfo_TracksPropertySource()
    {
        // Arrange & Act
        var property = new PropertyInfo
        {
            Name = "Content",
            TypeName = "System.String",
            Value = "Click Me",
            Source = "Local",
            IsBinding = false
        };

        // Assert
        property.Name.Should().Be("Content");
        property.TypeName.Should().Be("System.String");
        property.Value.Should().Be("Click Me");
        property.Source.Should().Be("Local");
        property.IsBinding.Should().BeFalse();
    }

    [Fact]
    public void BindingInfo_CapturesBindingDetails()
    {
        // Arrange & Act
        var binding = new BindingInfo
        {
            Property = "Text",
            Path = "UserName",
            Source = "ViewModel",
            Mode = "TwoWay",
            UpdateTrigger = "PropertyChanged",
            Status = "Active",
            CurrentValue = "John Doe"
        };

        // Assert
        binding.Property.Should().Be("Text");
        binding.Path.Should().Be("UserName");
        binding.Source.Should().Be("ViewModel");
        binding.Mode.Should().Be("TwoWay");
        binding.Status.Should().Be("Active");
    }

    [Fact]
    public void LayoutInfo_CapturesLayoutMetrics()
    {
        // Arrange & Act
        var layout = new LayoutInfo
        {
            ActualWidth = 800,
            ActualHeight = 600,
            DesiredSize = new SizeInfo { Width = 800, Height = 600 },
            RenderSize = new SizeInfo { Width = 800, Height = 600 },
            Margin = new ThicknessInfo { Left = 10, Top = 10, Right = 10, Bottom = 10 },
            Padding = new ThicknessInfo { Left = 5, Top = 5, Right = 5, Bottom = 5 },
            HorizontalAlignment = "Stretch",
            VerticalAlignment = "Stretch",
            Visibility = "Visible"
        };

        // Assert
        layout.ActualWidth.Should().Be(800);
        layout.ActualHeight.Should().Be(600);
        layout.Margin.Left.Should().Be(10);
        layout.Padding!.Top.Should().Be(5);
        layout.HorizontalAlignment.Should().Be("Stretch");
        layout.Visibility.Should().Be("Visible");
    }

    [Fact]
    public void ResourceInfo_CapturesResourceDetails()
    {
        // Arrange & Act
        var resource = new ResourceInfo
        {
            Key = "PrimaryBrush",
            TypeName = "System.Windows.Media.SolidColorBrush",
            Value = "#FF0078D7",
            Source = "App.xaml"
        };

        // Assert
        resource.Key.Should().Be("PrimaryBrush");
        resource.TypeName.Should().Contain("Brush");
        resource.Value.Should().StartWith("#");
        resource.Source.Should().Be("App.xaml");
    }

    [Fact]
    public void BindingError_CapturesErrorDetails()
    {
        // Arrange & Act
        var error = new BindingError
        {
            ElementHandle = "elem_123",
            ElementType = "System.Windows.Controls.TextBox",
            ElementName = "StatusTextBox",
            Property = "Text",
            BindingPath = "Statsu",
            ErrorType = "PathError",
            Message = "BindingExpression path error: 'Statsu' property not found on 'ViewModel'"
        };

        // Assert
        error.ElementName.Should().Be("StatusTextBox");
        error.Property.Should().Be("Text");
        error.BindingPath.Should().Be("Statsu");
        error.ErrorType.Should().Be("PathError");
        error.Message.Should().Contain("not found");
    }

    [Fact]
    public void ExportResult_CapturesExportData()
    {
        // Arrange & Act
        var export = new ExportResult
        {
            Format = "json",
            Content = "{\"root\": {}}",
            ElementCount = 42
        };

        // Assert
        export.Format.Should().Be("json");
        export.Content.Should().StartWith("{");
        export.ElementCount.Should().Be(42);
    }
}
