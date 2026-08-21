---
name: Ente Auth Community for Windows
description: A quiet native Windows instrument for finding and copying authenticator codes.
colors:
  ente-purple: "#8F33D6"
  quiet-fill: "#0F000000"
  quiet-stroke: "#18000000"
typography:
  title:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "20px"
    fontWeight: 600
    lineHeight: 1.25
  body:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.4
  code:
    fontFamily: "Cascadia Mono, Consolas, monospace"
    fontSize: "23px"
    fontWeight: 600
    lineHeight: 1.2
spacing:
  compact: "8px"
  row: "12px"
  control: "18px"
  section: "24px"
  page: "32px"
components:
  button-primary:
    backgroundColor: "{colors.ente-purple}"
    textColor: "#FFFFFF"
    typography: "{typography.body}"
    padding: "8px 16px"
  search-field:
    typography: "{typography.body}"
    height: "44px"
  code-row:
    backgroundColor: "transparent"
    typography: "{typography.body}"
    padding: "10px 12px"
---

# Design System: Ente Auth Community for Windows

## Overview

**Creative North Star: "The Quiet Code Ledger"**

The interface behaves like a precise native instrument that happens to hold authenticator codes. It is calm, dense enough for repeat use, and deliberately avoids a dashboard of decorative cards. Ente's purple remains recognizable but rare: it measures time and confirms primary intent rather than coloring whole surfaces.

The full window provides the current code workspace and settings; the tray panel is a compressed version of the same search-and-copy rhythm. Both surfaces inherit Windows theme, contrast, input, and focus behavior rather than simulating another platform.

**Key Characteristics:**

- A single readable ledger instead of a grid of cards.
- Native Windows chrome and system theme behavior.
- Account identity on the left, current code and time on the right.
- Secondary features remain quiet until requested.
- Privacy state is explicit and never encoded by color alone.

## Colors

The palette is system-neutral with one clear Ente-purple signal.

### Primary

- **Ente Signal Purple** (`#8F33D6`): Primary actions and the remaining-time line. It is not a decorative background.

### Neutral

- **Quiet Fill** (`#0F000000`): Optional low-emphasis surface tint in light contexts.
- **Quiet Stroke** (`#18000000`): Hairline separation where Windows' theme resources do not already provide structure.
- Background, text, disabled, error, and high-contrast colors come from WinUI theme resources so Windows remains the source of truth.

**The One Signal Rule.** Purple communicates the current action or time-sensitive state; it does not compete with account content.

## Typography

**Display Font:** Segoe UI Variable with Segoe UI fallback

**Body Font:** Segoe UI Variable with Segoe UI fallback
**Code Font:** Cascadia Mono with Consolas fallback

**Character:** Windows-native prose with a stable-width numeric readout. Monospace is reserved for the OTP value because alignment and recognition are functional there.

### Hierarchy

- **Title** (600, 20px, 1.25): Surface names such as Authenticator codes and Settings.
- **Body** (400, 14px, 1.4): Controls, explanations, and account names.
- **Row identity** (600, 15px): Issuer or service name.
- **Secondary** (400, 12–13px): Account identifier and compact status.
- **Code** (600, 19px in tray / 23px in window): Current OTP value only.

**The Numeric Reserve Rule.** Cascadia Mono never labels navigation, settings, or general content.

## Layout

The full window uses a compact native navigation rail and one main column. Page padding begins at 32px; repeated code rows use 10px vertical and 12px horizontal padding. Search is always above the ledger and remains 44px high. Collection-level Import, Export, and Add actions share the header; account-level pin, edit, and delete actions stay in each row's overflow menu. Export recommends the encrypted Ente-compatible path; choosing plaintext requires a second explicit warning. Destructive deletion requires confirmation. The main window starts at 820×780 and supports a useful compact width; text truncates before actions or codes collapse.

The tray panel is 380×560, anchored to the lower-right work area. Its header, search, ledger, and verification note form one vertical sequence. Editing and configuration leave the panel and open the full window.

Account connection stays in Settings rather than occupying the code ledger. The surface shows one truthful state—offline, connected, synchronizing, or sync failed—with Sign in, Sync now, and Sign out actions appearing only when relevant. Credentials and TOTP use native modal dialogs with inline validation; passkey-only accounts receive an explicit limitation instead of a broken control.

## Elevation & Depth

The system is flat by default. Structure comes from platform backgrounds, spacing, and separators. Always-on-top behavior gives the tray panel operational elevation; decorative shadows are unnecessary.

**The Flat-at-Rest Rule.** Do not wrap every code or settings row in a lifted container.

## Shapes

Controls inherit WinUI's native corner geometry and focus visuals. Code rows remain rectangular ledger entries. Circular progress indicators, pills around codes, and oversized rounded cards are outside this system.

## Components

### Buttons

- **Primary:** WinUI accent button using Ente Signal Purple and an explicit action label.
- **Secondary:** Native neutral button; icon-only buttons require a tooltip and accessible name.
- **State:** Preserve native hover, pressed, disabled, keyboard focus, and high-contrast rendering.

### Inputs / Fields

- **Style:** Native `AutoSuggestBox`, 44px tall, with a search icon and plain-language placeholder.
- **Focus:** Windows focus visual remains visible; filtering updates without hiding the field.
- **Error:** Explain why an `otpauth://` link failed and keep the user's input recoverable.

### Navigation

The rail uses icon plus text when expanded and familiar Fluent glyphs when compact. Codes and Settings retain stable positions; later destinations join only when their screens work. Selection uses native NavigationView state rather than a custom colored strip.

### Code Ledger Row

Issuer and account form the identity group. The OTP value and thin time line form the action group. Copy is a 40px explicit button in the full window; the entire row is the copy target in the tray panel.

### Tray Quick Panel

The panel opens only after Windows Hello succeeds, never appears in the task switcher, closes on focus loss, and contains only search-and-copy functionality plus a route to the full app.

## Do's and Don'ts

### Do:

- **Do** keep search and the current OTP legible within the first scan.
- **Do** use native theme and high-contrast resources for system-owned color roles.
- **Do** pair every icon-only action with a tooltip and accessible name.
- **Do** clear sensitive tray content when the app locks.
- **Do** keep motion short, interruptible, and absent under reduced-motion settings.

### Don't:

- **Don't** turn authenticator entries into same-sized floating cards.
- **Don't** expose issuer, account, or code content before Windows Hello verifies the tray request.
- **Don't** use purple as a general background or visual decoration.
- **Don't** hide Quit behind window close; closing hides to tray and Quit remains explicit.
- **Don't** imply that this community fork is endorsed or audited by Ente.
