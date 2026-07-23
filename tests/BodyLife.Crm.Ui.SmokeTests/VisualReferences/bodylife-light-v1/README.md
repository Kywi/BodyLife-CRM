# BodyLife light v1 visual reference lock

This directory is an immutable copy of the 12 approved Phase 0 artifacts. `manifest.json` is the machine-readable source of filenames, types, dimensions and SHA-256 values. Recalculate every listed SHA-256 before using a reference; any mismatch is a hard stop requiring external-delivery re-verification and recorded user approval.

The HTML is the rendered reference source for palette and layout extraction. It declares a system sans stack (`-apple-system`, `system-ui`, `Segoe UI`), 14px base type and 12.5px base radius. The delivered desktop capture was outer `768x1100`, inner/iframe `736 CSS px`, DPR `1.5`, producing `1104px` output. Mobile was outer `352x1400`, inner `320 CSS px`, DPR `1.5`, producing `480px` output. It was delivered from Chrome on Windows; the executable is known but its exact version is not. The template business date/source is `2026-07-16`.

Target tokens: white canvas; `#1a1c1f` text; reference blue `#339cff` as a visual source (implementation uses a darker accessible filled-navigation blue); success, warning, destructive red-orange and violet semantics; 14px base type; 12.5px/20px radii; 108px + 14px app grid; 190–210px + 14px work rail; 12px stack; 76px desktop/56px mobile logo. Repeat captures must record browser version, OS and available font resolution with the test evidence.

Mask only volatile timestamps, generated identifiers, and authenticated-session/device suffixes. Never mask navigation, primary composition, warnings, actions, brand/logo, semantic colors, focus states or responsive ordering.
