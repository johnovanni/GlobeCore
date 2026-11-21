using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace GlobeCore.Wpf.Layers;

/// <summary>One line of a layer's legend: a colour swatch (or a glyph drawn in that colour) and what it means.</summary>
public sealed record LegendItem(string Label, Color Color, string? Glyph);

/// <summary>
/// A layer of the globe, owned by a <see cref="LayerHost"/>. <see cref="ZOrder"/> fixes the order layers of one kind are
/// drawn and picked in (higher is drawn later, and wins a pick). Hiding a layer (<see cref="IsVisible"/>) leaves it out of
/// the image, the scene, the overlay and picking. Used on the UI thread only.
/// </summary>
public abstract class GlobeLayer : INotifyPropertyChanged
{
    private bool _isVisible = true;
    private string? _source;
    private DateTime? _asOfUtc;
    private IReadOnlyList<LegendItem> _legendItems = [];

    protected GlobeLayer(string id, string title, int zOrder)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        ZOrder = zOrder;
    }

    public string Id { get; }
    public string Title { get; }
    public int ZOrder { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    /// <summary>Where the layer's data comes from (for the legend and credits), if anywhere.</summary>
    public string? Source
    {
        get => _source;
        protected set => SetField(ref _source, value);
    }

    /// <summary>The UTC time the shown data is for, if it has one.</summary>
    public DateTime? AsOfUtc
    {
        get => _asOfUtc;
        protected set => SetField(ref _asOfUtc, value);
    }

    public IReadOnlyList<LegendItem> LegendItems
    {
        get => _legendItems;
        protected set => SetField(ref _legendItems, value ?? []);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raised by the layer kinds' Invalidate methods; the host listens. The flag: compose at once, unthrottled.</summary>
    internal event Action<GlobeLayer, bool>? Invalidated;

    private protected void RaiseInvalidated(bool immediate) => Invalidated?.Invoke(this, immediate);
}
