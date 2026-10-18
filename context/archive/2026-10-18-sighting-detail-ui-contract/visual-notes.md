# Visual notes: sighting detail UI contract

## Method

Captured 2026-10-18 on `/dev/kitchen-sink` from a fresh `ng serve` on port 4200 serving the working tree after Phase 2 (commit `00b08ac`) plus the uncommitted Phase 3 sink group, in the Claude browser pane with viewport emulation at 375×812, 768×1024 (`md`) and 1280×900 (`lg`). "Measured" means read from the DOM (`getBoundingClientRect`, computed styles, `document.activeElement`); Tab presses and clicks were real input in the pane. Offsets are px from the top of the `app-page-card` of each cell.

Limits: the sink caps a cell at 640 px and pads it, so the card is 309 px wide at 375 (a real page would be about 343 px); a 375 claim from the sink is conservative. The detail page is deliberately kept at its current 28rem card width: width on large screens is owned by the separate change `app-wide-large-screen-layout`, not by this one. No screen reader was run.

## Card sizes by state

| Cell | Card height at 375 | `md` 768 | `lg` 1280 |
| --- | --- | --- | --- |
| Loading (`detail-loading`) | 193 | 203 | 203 |
| Not found (`detail-not-found`) | 227 | 216 | 216 |
| Error (`detail-error`) | 291 | 280 | 280 |
| Loaded (`detail-loaded`) | 680 | 690 | 690 |
| Loaded, long name (`detail-long-name`) | 770 | 810 | 810 |
| Map shown (`detail-map-shown`) | 1064 | 1074 | 1074 |

Card width is 309 px at 375 and 448 px (28rem) at `md` and `lg`. No cell has horizontal overflow at any width; the document has no horizontal scroll.

## Layout stability

- **Loading to loaded.** The card grows from 193 to 680 px at 375 (203 to 690 at `md` and `lg`): nothing reserves space while loading. This is accepted for now: it is a first-load jump on a page the user just navigated to, with no content shifting under a pointer. Charge 2 named it; it is recorded here, not fixed.
- **Heading wrap.** The long name wraps to 4 lines at every width, adding 90 px to the card at 375 and 120 px at `md` and `lg` (heading 160 px against 40 px for one line at `md` and `lg`). The heading does not truncate or overflow.
- **Map open.** The map host starts 590 px below the card top at 375 (600 at `md`/`lg`) and is 384 px high (24rem), a user-initiated shift. The toggle sits at 526 (536) in both Show and Hide states, and its width changes by 6 px (127 to 121) with the label.
- **Back link.** In every cell the Back link is inside the card's footer (`backInsideCard` true): at 150 px below the card top while loading, 163 not found, 227 error, 637 loaded, 1021 with the map open at `lg`. The earlier outside-the-card row and its width mismatch no longer exist.

## 7-state matrix

| State | Applies | Evidence |
| --- | --- | --- |
| default | yes | `detail-loaded`: seven rows, token-coloured criticality badge, Show map |
| hover | yes | `detail-error`: not confirmed visually: a synthetic hover on "Try again" (`:hover` true) showed no visible tint in the pane. Material state layer, no page-specific code; deferred |
| focus-visible | yes | `detail-map-shown` (Tab pass below): visible ring on the Back link (screenshot), 2 px solid outline on the map region, zoom controls and attribution link. The Show map toggle and Try again were not measured (see Not verified) |
| disabled | N/A | No control on this page is ever disabled: the toggle and the retry button are always enabled and there is no form |
| error | yes | `detail-not-found` (info alert, no retry) and `detail-error` (alert plus Try again, 280 px); field error N/A |
| empty | N/A | No list on this page; an unnamed sighting is a loaded variant, shown in `detail-unnamed` ("Unnamed bird") |
| loading | yes | `detail-loading`: heading "Sighting" and the loading indicator |

## Keyboard Tab pass

Real Tab presses from the "Map shown" cell at 375, measuring `document.activeElement` at each step.

- **Order:** Hide map → map region ("Map showing where this sighting was reported") → Zoom in → Zoom out → attribution link → Back to map. There is no stop on the marker.
- **Rings:** the map region, zoom controls and attribution link each show a 2 px solid outline. Back to map shows Material's focus ring (visible in a screenshot; the button's own `outline` is `none`, as on the other Material buttons).
- **Programmatic focus.** In `detail-error-then-loaded`, pressing the manual header button ("Press Try again") retries and moves focus to the `h1` ("Limpy") once the sighting loads, as the plan specifies. After a pointer-initiated press the heading shows no ring (`outline-style: none`), which is how `:focus-visible` treats programmatic focus after a click. A keyboard-initiated retry was not measured.

## Sink does not take focus on load

After a fresh load of `/dev/kitchen-sink` with all nine detail cells, `document.activeElement` is `BODY` and `scrollY` is 0. The first-arrival focus gate (`Router.currentNavigation()` read in the constructor) therefore does not fire for cells created while the sink's own navigation is in flight, and the `id`-param fallback was not needed.

## Not verified

- A visible hover state on Material buttons (see the matrix).
- The focus ring on the Show map toggle and on Try again: the Tab pass started in `detail-map-shown`, so it began at "Hide map", not at "Show map", and no cell with Try again was tabbed to.
- A keyboard-initiated retry: the heading's focus ring after programmatic focus.
- Entering the sink by an in-app navigation (a link, not a reload): the "takes no focus on load" check above covers a fresh load only, and the sink spec has no router, so `currentNavigation()` is always null there.
- A full-group screenshot at desktop and 375 px: only screenshots of single cells were looked at.
- The map-library failure cell against the real map: the sink spec renders the page over `MapViewStub`, which never fails. The cell `detail-map-load-failed` showed the page, the map failure message and Try again inside the card in the browser (`mapErr` and `retry` true, 1295 px cell height).
- A screen reader.
- The real page at 375 px (the sink card is 309 px wide).
