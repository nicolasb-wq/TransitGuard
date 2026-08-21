import 'package:flutter/material.dart';

/// Farb- und Formsprache der App — spiegelt bewusst das Design-System der PWA
/// (web/src/styles.css), damit beide Kanäle sich gleich anfühlen.
/// Hell UND dunkel: ÖPNV wird auch bei Tageslicht draußen benutzt.
class TgTheme {
  static const seed = Color(0xFF0B6EA8);
  static const warn = Color(0xFFB3261E);
  static const warnDark = Color(0xFFFF8A80);
  static const ok = Color(0xFF0A6C4A);
  static const okDark = Color(0xFF5DDAA4);

  /// Mindest-Trefferfläche (1-Hand-Bedienung).
  static const tap = 48.0;

  static ThemeData of(Brightness helligkeit) {
    final schema = ColorScheme.fromSeed(seedColor: seed, brightness: helligkeit);
    return ThemeData(
      useMaterial3: true,
      colorScheme: schema,
      scaffoldBackgroundColor: schema.surface,
      cardTheme: CardThemeData(
        elevation: 0,
        margin: const EdgeInsets.only(bottom: 14),
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(20),
          side: BorderSide(color: schema.outlineVariant),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size(0, tap),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
          textStyle: const TextStyle(fontWeight: FontWeight.w600, fontSize: 16),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(0, tap),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: schema.surfaceContainerHighest,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: BorderSide(color: schema.outlineVariant),
        ),
        contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      ),
      chipTheme: ChipThemeData(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(999)),
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
      ),
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
      ),
    );
  }
}

/// Platzhalter in der Form des erwarteten Inhalts — kein Spinner, kein Sprung.
class Skeleton extends StatefulWidget {
  final double breite, hoehe, radius;
  const Skeleton({super.key, this.breite = double.infinity, this.hoehe = 14, this.radius = 8});

  @override
  State<Skeleton> createState() => _SkeletonState();
}

class _SkeletonState extends State<Skeleton> with SingleTickerProviderStateMixin {
  late final AnimationController _c =
      AnimationController(vsync: this, duration: const Duration(milliseconds: 1300))..repeat();

  @override
  void dispose() { _c.dispose(); super.dispose(); }

  @override
  Widget build(BuildContext context) {
    final basis = Theme.of(context).colorScheme.surfaceContainerHighest;
    final glanz = Theme.of(context).colorScheme.outlineVariant;
    // Nutzende mit reduzierter Bewegung bekommen eine ruhige Fläche.
    final ruhig = MediaQuery.maybeDisableAnimationsOf(context) ?? false;
    if (ruhig) {
      return _flaeche(basis, null);
    }
    return AnimatedBuilder(
      animation: _c,
      builder: (_, __) => _flaeche(basis, LinearGradient(
        begin: Alignment(-1 + _c.value * 2, 0),
        end: Alignment(_c.value * 2, 0),
        colors: [basis, glanz, basis],
      )),
    );
  }

  Widget _flaeche(Color basis, Gradient? verlauf) => Container(
        width: widget.breite,
        height: widget.hoehe,
        decoration: BoxDecoration(
          color: verlauf == null ? basis : null,
          gradient: verlauf,
          borderRadius: BorderRadius.circular(widget.radius),
        ),
      );
}

/// Linienplakette (U1, S3 …) — gleiche Form wie in der PWA.
class RoutePlakette extends StatelessWidget {
  final String linie;
  const RoutePlakette(this.linie, {super.key});
  @override
  Widget build(BuildContext context) {
    final s = Theme.of(context).colorScheme;
    return Container(
      constraints: const BoxConstraints(minWidth: 46),
      height: 30,
      alignment: Alignment.center,
      padding: const EdgeInsets.symmetric(horizontal: 10),
      decoration: BoxDecoration(color: s.primary, borderRadius: BorderRadius.circular(8)),
      child: Text(linie,
          style: TextStyle(color: s.onPrimary, fontWeight: FontWeight.w700, fontSize: 14)),
    );
  }
}
