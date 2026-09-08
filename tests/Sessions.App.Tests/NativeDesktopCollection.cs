namespace Sessions.App.Tests;

// Native fixtures share desktop/process enumeration; do not let temporary apps from one distort another.
[CollectionDefinition("Native desktop", DisableParallelization = true)]
public sealed class NativeDesktopCollection;
