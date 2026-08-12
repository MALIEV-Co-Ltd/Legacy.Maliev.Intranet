# Shadcn Form, Toolbar, and Customer Detail Polish

## Outcome

Polish the shared list toolbar, all MudBlazor form controls inside the Shadcn scope, and the customer detail page so they read as one dense, calm MALIEV operations system. Preserve every route, query value, event callback, authorization check, BFF contract, localized string, and save/cancel behavior.

## Shared form system

The package adapter remains the single owner of input appearance. Outlined Mud fields must render like Shadcn fields: a static label above a single 1px control border, 36px desktop height, compact horizontal padding, aligned adornments, a restrained 3px semantic focus ring, and helper/error text below without shifting or crossing the control. The same contract applies to text, numeric, select, date, textarea, disabled, readonly, required, and invalid states. Touch layouts retain the existing 44px minimum.

## Shared list toolbar

`ListToolbar` becomes a quiet command strip rather than a boxed mini-form. Search stays dominant, sort and page-size remain bounded, and Clear/Refresh form a distinct action cluster. Desktop remains one dense row where space permits. Tablet uses two balanced rows without orphaning Refresh. Mobile uses full-width search, paired sort/page-size, then full-width or stacked actions as localization requires. No consumer-specific API or behavior changes are introduced.

## Customer detail page

The page uses a deliberate record hierarchy: title and customer number with the edit action; a primary Contact section; secondary Company and Address sections; and compact audit metadata. Definition data aligns consistently and missing values remain explicit. Inline editing stays within Contact, but fields use a responsive two-column form grid on wide screens and one column on narrow screens. The action row is clearly separated from fields. Loading, error, readonly, editing, submitting, validation, and permission states retain existing behavior.

## Accessibility and responsive requirements

- WCAG 2.2 AA contrast and visible focus.
- 44px targets for coarse pointers and narrow layouts; 36px desktop control density.
- No horizontal document overflow at 1280, 768, 390, or 320 CSS pixels.
- English and Thai labels must not collide with controls or actions.
- Keyboard order follows visual order; hidden responsive content is not focusable.
- Reduced motion and existing semantic live regions remain intact.

## Verification

Add failing source/package contracts before adapter or component changes, then production-browser checks using real rendered MudBlazor DOM. Verify representative list consumers and the real customer detail route at desktop, tablet, and mobile sizes, including edit mode, date picker, invalid fields, disabled submitting state, dark mode, and Thai localization. Finish with Release build, focused tests, full Legacy tests, package tests, browser suite, detector, diff audit, and a scoped commit.
