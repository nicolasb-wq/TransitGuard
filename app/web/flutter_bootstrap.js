// ---------------------------------------------------------------------------
// Eigener Bootstrap (dokumentierter Anpassungspunkt von Flutter Web).
//
// Grund: Standardmäßig lädt Flutter Web CanvasKit von www.gstatic.com. In
// abgeschotteten Umgebungen — etwa dem Build-Container — blockt der Proxy das
// mit ERR_CONNECTION_RESET, und die App bootet überhaupt nicht. Für den
// Release-Build löst `--no-web-resources-cdn` das; im Debug-Build (den
// `flutter drive` nutzt) greift der Schalter nicht. Deshalb wird die Quelle
// hier fest auf die mitgelieferten Dateien gezeigt.
//
// Am 22.08.2026 gemessen: ohne diese Zeile wartet `flutter drive` endlos auf
// eine App, die nie startet.
// ---------------------------------------------------------------------------
{{flutter_js}}
{{flutter_build_config}}

_flutter.loader.load({
  config: {
    canvasKitBaseUrl: 'canvaskit/',
  },
});
