// Blazor JS initializer for MauiDevFlow.Blazor
// Auto-discovered by the Blazor framework in MAUI Blazor Hybrid apps.
// Uses the classic pattern: beforeStart / afterStarted (NOT beforeWebStart/afterWebStarted).
//
// This eliminates the need for consuming apps to manually add a <script> tag
// for chobitsu.js in their wwwroot/index.html.

export function beforeStart(options, extensions) {
    // Inject chobitsu.js before Blazor starts.
    // In MAUI Blazor Hybrid, RCL static web assets are served from the wwwroot root.
    var script = document.createElement('script');
    script.setAttribute('src', 'chobitsu.js');
    document.head.appendChild(script);
    console.log('[MauiDevFlow.Blazor] chobitsu.js injected via JS initializer');
}

export function afterStarted(blazor) {
    console.log('[MauiDevFlow.Blazor] Blazor started');
}
