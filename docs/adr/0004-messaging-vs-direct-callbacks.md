# IMessenger reserved for many-to-many; direct callbacks for single consumer

`CommunityToolkit.Mvvm.Messaging.IMessenger` is used in this codebase
only when an event has multiple unrelated consumers — currently
`StudentHoverMessage` (dotplot ↔ violin sync), `EditCommentRequestMessage`,
and `RenderWithThemeMessage`. When an event has exactly one consumer
(e.g. Settings dialog → MainWindowViewModel), use a constructor-injected
`Action<…>` callback or a direct method reference instead. IMessenger
ceremony for a single consumer adds indirection without value.

## When to revisit

If a second consumer of an event currently delivered via direct
callback emerges, switch that event to IMessenger.
