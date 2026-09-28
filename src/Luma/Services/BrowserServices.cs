using Luma.Authentication;
using Luma.Updates;

namespace Luma.Services;

public interface ILumaStateStore
{
    LumaState State { get; }
    void Save();
}

public sealed class JsonLumaStateStore : ILumaStateStore
{
    public LumaState State { get; } = LumaState.Load();
    public void Save() => State.Save();
}

public sealed class InMemoryLumaStateStore(LumaState? state = null) : ILumaStateStore
{
    public LumaState State { get; } = state ?? new LumaState();
    public int SaveCount { get; private set; }
    public void Save() => SaveCount++;
}

public interface IClock { DateTime UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }

public sealed class BrowserServices
{
    public required ILumaStateStore StateStore { get; init; }
    public required IClock Clock { get; init; }
    public required IAuthService Auth { get; init; }
    public required IUpdateService Updates { get; init; }
    public static BrowserServices CreateDefault() => new()
    {
        StateStore = new JsonLumaStateStore(), Clock = new SystemClock(),
        Auth = new SupabaseAuthService(SupabaseOptions.Load(), new ProtectedAuthSessionStore()),
        Updates = new BackgroundUpdateService(UpdateOptions.Load())
    };
}
