namespace GlobeCore.Storage;

/// <summary>Loads and saves an app's settings record (the app defines <typeparamref name="T"/>).</summary>
public interface ISettingsStore<T> where T : class
{
    T Load();
    void Save(T settings);
}
