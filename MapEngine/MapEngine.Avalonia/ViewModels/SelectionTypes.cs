using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Data;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.Services;
using MapEngine.Core.Hosting;

namespace MapEngine.Avalonia.ViewModels;

public interface IGlobalSelectionItem
{
    string DisplayName { get; }

    string SelectionType { get; }
}

public enum SelectionNavigationTarget
{
    Map,
    AssetLibrary
}

public sealed class SelectionNavigationRequestEventArgs : EventArgs
{
    public SelectionNavigationRequestEventArgs(SelectionNavigationTarget target, IGlobalSelectionItem selection)
    {
        Target = target;
        Selection = selection;
    }

    public SelectionNavigationTarget Target { get; }

    public IGlobalSelectionItem Selection { get; }
}
