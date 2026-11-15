using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CityHelper.ViewModels;

namespace CityHelper;

/// <summary>Maps FooViewModel -> CityHelper.Views.FooView by naming convention.</summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null) return null;
        var name = param.GetType().FullName!.Replace("ViewModels", "Views").Replace("ViewModel", "View");
        var type = Type.GetType(name);
        return type != null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "View not found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
