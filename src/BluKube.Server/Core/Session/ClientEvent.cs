namespace BluKube.Server.Core.Session;

public sealed record SearchEvent(string Query, int Limit = 20);
public sealed record PlayEvent(string VideoId);
public sealed record PauseEvent();
public sealed record ResumeEvent();
public sealed record SetVolumeEvent(float Value);
public sealed record SeekToEvent(double Seconds);

public union ClientEvent(
    SearchEvent,
    PlayEvent,
    PauseEvent,
    ResumeEvent,
    SetVolumeEvent,
    SeekToEvent
);