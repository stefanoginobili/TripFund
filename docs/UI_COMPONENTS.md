# UI Components & Common Rules

This document outlines the standard rules and behaviors for UI components across the application. These rules must be strictly adhered to in order to maintain a consistent user experience.

## 1. Input Controls
### Readonly State
- Must visually appear "disabled".
- Both background and foreground must be grayed out.
- For `text` or `number` inputs, the inner text **must remain selectable**.
- When focused, no styling changes (borders, colors, etc.) should be applied.
- If the input is empty, no placeholder must be shown.

### Writable State
- Background must be white.
- Foreground value must be black (the thematic dark green `#1A3C2F` is an acceptable alternative).
- Any accompanying icons (e.g., dates/times) must be green, matching the application's theme.
- **Focus Behavior:**
  - The border must be highlighted according to the application's theme (green).
  - The view must scroll or adjust to ensure the input is visible within the viewport.
- **Data Types:**
  - `number`: The inner value must be center-aligned (unless an explicit exception is made), and focusing the input must automatically select all text.
  - `text`: The inner value must be left-aligned (unless an explicit exception is made). **All user-provided text must be trimmed (whitespace removed from both ends) when focus is lost and before being persisted.**

## 2. DropDown Lists
### Readonly State
- Must visually appear "disabled" and be grayed out (background and foreground).
- Clicking the trigger must have no effect.

### Writable State
- Background must be white, with foreground text in black (or the thematic dark green `#1A3C2F`).
- Items are permitted to have icons.
- **Interaction:**
  - The trigger must maintain the same height regardless of whether an item is selected or a placeholder is shown.
  - Clicking the trigger opens the item list. It must appear above or below the trigger depending on available viewport space.
  - If a value is already selected when opened, the list must automatically scroll to ensure the selected item is visible in the middle.
  - The currently selected item must be highlighted with a pastel green background matching the app theme (no extra checkmark glyphs required).
  - Clicking anywhere on the screen (inside or outside the list boundaries) must close the dropdown.

## 3. Context Menus
- **Trigger:** Always represented by a three-dot vertical icon.
- **Items:** White background, black (or the thematic dark green `#1A3C2F`) foreground. Small, simple, single-colored icons are allowed in front of the text.
- **Disabled Items:** Must be grayed out.
- **Destructive Actions:** Menu items leading to deletion or removal must have a red foreground. Selecting them must trigger a confirmation modal before any deletion occurs.
- **Interaction:** Clicking anywhere on the screen (inside or outside the menu boundaries) must close the context menu.

## 4. Buttons & Toggles
- **Primary Actions:** Must use the primary theme color (green) with white text.
- **Secondary Actions:** Should have a transparent or white background with themed borders/text, or use a neutral gray.
- **Disabled State:** Buttons and toggles must be grayed out and visually unresponsive.
- **Toggles/Switches:** Active (ON) state must be highlighted in the primary theme color (green). Inactive (OFF) state should be a neutral gray.

## 5. Error & Loading States
- **Validation Errors:** Invalid inputs must be highlighted with a red border. A clear, concise error message (in Italian) must be displayed immediately below the input.
- **Loading States:** Actions that require processing (e.g., submitting a form) must disable the trigger button and display a loading spinner. The spinner can be placed inside the button alongside or replacing the text.

## 6. Modals & Dialogs
- **Backdrop:** Must feature a semi-transparent dark overlay background to obscure the underlying content.
- **Dismissal:** 
  - Standard modals should be closable by clicking the outside overlay or an explicit close button/icon.
  - Destructive or critical confirmation modals must require explicit button clicks ("Annulla" / "Conferma") and must not close by clicking the backdrop.
- **Scroll:** If modal content exceeds viewport height, the modal body must scroll while keeping the header and action buttons fixed.

## 7. Accessibility & Focus (A11y)
- **Focus Outlines:** Interactive elements navigated via keyboard must display a clear focus outline to indicate the active element.
- **ARIA Labels:** Icons functioning as buttons (without visible text) must include appropriate ARIA labels for screen readers.
- **Contrast:** Ensure sufficient contrast ratios between text/icons and their backgrounds, particularly for disabled states, so they remain legible.

## 8. Global Styling & CSS
- **Centralized Styles:** Whenever working with application-wide styles, and when it is possible to match the feature requirements, styles must be defined in a centralized way (e.g., in global CSS files like `app.css` or through shared component abstractions).
- **UI Coherence:** Avoid redundant local CSS overrides or inline styles that duplicate common patterns. Centralized styling ensures that the UI remains visually coherent and maintainable across all application pages.
