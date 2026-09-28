# Luma architecture

## Composition
`App` is the composition root. It creates `BrowserServices` once and injects it into each `MainWindow`. Runtime state and time are accessed through `ILumaStateStore` and `IClock`, so tests can use `InMemoryLumaStateStore` and a fake clock.

## UI modules
`MainWindow.xaml.cs` now contains only window state, construction and lifecycle. Feature code lives in focused partial modules: Browser, Assistant, WebBridge, Navigation, Panes, Sidebar, Session, ContentTools, Translation, InternalPages, WindowChrome and Events. Partial files are intentionally an intermediate presentation-layer boundary: they preserve WPF behavior while making each concern reviewable. New business logic must not be added directly to these files.

## Core
`Luma.Core` targets plain `net8.0` and has no WPF/WebView2 dependency. URL normalization, tracking cleanup, site identity and suggestion scoring live here and are covered by `Luma.Core.Tests`. Future domain policies belong in Core.

## Infrastructure
JSON persistence remains behind `ILumaStateStore`; WebView2 and registry operations remain in the WPF shell. The next extraction candidates are translation, history and download policies.

## Dependency rule
`Luma` may reference `Luma.Core`; Core must never reference WPF, WebView2, registry, files, or Luma UI types. Tests reference Core only.
