import {
  CellStep,
  choose,
  click,
  fill,
  keydown,
  press,
  pressLabelled,
  repeat,
  submitForm,
  waitFor,
  waitForText,
} from './cell-driver';
import {
  HeaderModes,
  MessagesModes,
  NearbyMapModes,
  ProfileModes,
  SightingDetailModes,
  SightingListModes,
  StubModes,
} from './cell-stubs';

export interface CellScenario {
  readonly action: string;
  readonly steps: readonly CellStep[];
  readonly trigger?: CellStep;
}

interface CellBase {
  readonly id: string;
  readonly label: string;
  readonly steps: readonly CellStep[];
  readonly dialog?: CellScenario;
  readonly manual?: CellScenario;
}

export type ProfileCell = CellBase & ProfileModes;
export type NearbyMapCell = CellBase & NearbyMapModes;
export type SightingDetailCell = CellBase & SightingDetailModes;
export type SightingListCell = CellBase & SightingListModes;
export type MessagesCell = CellBase & MessagesModes;
export type HeaderCell = CellBase & HeaderModes;
export type CellDefinition = CellBase & StubModes;

export interface CellGroup {
  readonly title: string;
  readonly wide: boolean;
  readonly cells: readonly CellDefinition[];
}

const nameField = 'mat-form-field input';
const passwordFields = 'input[type="password"]';
const fieldError = 'mat-form-field mat-error';
const busyButton = 'app-submit-button [aria-busy="true"]';
const validPasswords = ['Old1!pass', 'New1!passw', 'New1!passw'];

function submittedName(value: string, ...outcome: CellStep[]): CellStep[] {
  return [press('profile.edit'), fill(nameField, [value]), submitForm(), ...outcome];
}

const profileCells: readonly ProfileCell[] = [
  {
    id: 'read-only',
    label: 'Read-only',
    group: 'profile',
    save: 'success',
    password: 'success',
    steps: [],
  },
  {
    id: 'read-only-my-sightings',
    label: 'Read-only, My sightings with items (criticality badges)',
    group: 'profile',
    save: 'success',
    password: 'success',
    mine: 'items',
    steps: [],
  },
  {
    id: 'edit-pristine',
    label: 'Edit, pristine (Save disabled)',
    group: 'profile',
    save: 'success',
    password: 'success',
    steps: [press('profile.edit'), waitFor(nameField)],
  },
  {
    id: 'edit-client-error',
    label: 'Edit, client validation error',
    group: 'profile',
    save: 'success',
    password: 'success',
    steps: submittedName('ab', waitFor(fieldError)),
  },
  {
    id: 'edit-duplicate-name',
    label: 'Edit, server error (DuplicateDisplayName)',
    group: 'profile',
    save: 'duplicate',
    password: 'success',
    steps: submittedName('Taken', waitFor(fieldError)),
  },
  {
    id: 'edit-form-error',
    label: 'Edit, server error (form-level)',
    group: 'profile',
    save: 'failure',
    password: 'success',
    steps: submittedName('Valid name', waitFor('app-alert[role="alert"]')),
  },
  {
    id: 'saving',
    label: 'Saving (request never completes)',
    group: 'profile',
    save: 'pending',
    password: 'success',
    steps: submittedName('Valid name', waitFor(busyButton)),
  },
  {
    id: 'saved',
    label: 'Saved',
    group: 'profile',
    save: 'success',
    password: 'success',
    steps: submittedName('Jan Kowalski', waitFor('app-alert[role="status"]')),
  },
  {
    id: 'password-changed',
    label: 'Password changed',
    group: 'profile',
    save: 'success',
    password: 'success',
    steps: [],
    dialog: {
      action: 'Run password change',
      steps: [fill(passwordFields, validPasswords), submitForm()],
    },
  },
  {
    id: 'saved-then-password-changed',
    label: 'Saved, then password changed (one notice at a time)',
    group: 'profile',
    save: 'success',
    password: 'success',
    steps: submittedName('Jan Kowalski', waitFor('app-alert[role="status"]')),
    dialog: {
      action: 'Run password change',
      steps: [fill(passwordFields, validPasswords), submitForm()],
    },
  },
];

const dialogCells: readonly ProfileCell[] = [
  {
    id: 'dialog-default',
    label: 'Dialog, default',
    group: 'profile',
    save: 'success',
    password: 'success',
    steps: [],
    dialog: { action: 'Open dialog', steps: [waitFor(passwordFields)] },
  },
  {
    id: 'dialog-field-errors',
    label: 'Dialog, field errors',
    group: 'profile',
    save: 'success',
    password: 'success',
    steps: [],
    dialog: {
      action: 'Open dialog',
      steps: [fill(passwordFields, ['Old1!pass', 'abc', 'xyz']), submitForm(), waitFor(fieldError)],
    },
  },
  {
    id: 'dialog-wrong-current',
    label: 'Dialog, wrong current password',
    group: 'profile',
    save: 'success',
    password: 'wrongCurrent',
    steps: [],
    dialog: {
      action: 'Open dialog',
      steps: [fill(passwordFields, validPasswords), submitForm(), waitFor(fieldError)],
    },
  },
  {
    id: 'dialog-busy',
    label: 'Dialog, busy (request held for 30 seconds)',
    group: 'profile',
    save: 'success',
    password: 'pending',
    steps: [],
    dialog: {
      action: 'Open dialog',
      steps: [fill(passwordFields, validPasswords), submitForm(), waitFor(busyButton)],
    },
  },
];

const marker = '.pw-map-marker--urgent';
const status = '[role="status"]';
const zoomIn = '.leaflet-control-zoom-in';
const countryToSearchZoomSteps = 4;
const statusFilter = 'app-sighting-filters mat-form-field:first-of-type mat-select';
const criticalityFilter = 'app-sighting-filters mat-form-field:last-of-type mat-select';

const nearbyMapCells: readonly NearbyMapCell[] = [
  {
    id: 'map-locating',
    label: 'Locating (geolocation never answers)',
    group: 'nearbyMap',
    geolocation: 'pending',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-zoomed-out',
    label: 'Position unknown at country zoom (location denied, Search disabled)',
    group: 'nearbyMap',
    geolocation: 'denied',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-hint',
    label: 'Position unknown, zoomed in to level 10 (hint, Search enabled)',
    group: 'nearbyMap',
    geolocation: 'denied',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [...repeat(countryToSearchZoomSteps, click(zoomIn)), waitForText(status, 'map.hint')],
  },
  {
    id: 'map-use-my-location',
    label: 'Location timed out ("Locate me", next answer chosen in the header)',
    group: 'nearbyMap',
    geolocation: 'timeoutThenNext',
    nextLocation: 'ok',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-retry-locating',
    label: 'Retrying location ("Locate me" disabled while locating)',
    group: 'nearbyMap',
    geolocation: 'timeoutThenNext',
    nextLocation: 'pending',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [press('map.locateMe'), waitForText(status, 'map.locating')],
  },
  {
    id: 'map-loading',
    label: 'Loading, first search (nearby request never completes)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'pending',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-loading-kept',
    label: 'Loading again (previous markers kept, Search disabled)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'itemsThenPending',
    leaflet: 'tileless',
    steps: [press('map.searchArea'), waitFor(`${status} app-loading-indicator`)],
  },
  {
    id: 'map-loaded',
    label: 'Found (default: legend and prompt in the aside)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-selected',
    label: 'Selected by pointer (marker clicked)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
    manual: { action: 'Click a marker', steps: [click(marker), waitFor('mat-card')] },
  },
  {
    id: 'map-selected-keyboard',
    label: 'Selected with the keyboard (Enter on a focused marker)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
    manual: {
      action: 'Press Enter on a marker',
      steps: [keydown(marker, 'Enter'), waitFor('mat-card')],
    },
  },
  {
    id: 'map-filtered',
    label: 'Filtered by criticality (filter row opened, Urgent chosen from the cell header)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'filtered',
    leaflet: 'tileless',
    steps: [],
    manual: {
      action: 'Choose Urgent',
      steps: [
        press('map.filters.show'),
        choose(criticalityFilter, 'sighting.criticality.urgent'),
      ],
    },
  },
  {
    id: 'map-filtered-no-match',
    label: 'Filter with no match (At the vet chosen from the cell header)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'filtered',
    leaflet: 'tileless',
    steps: [],
    manual: {
      action: 'Choose At the vet',
      steps: [press('map.filters.show'), choose(statusFilter, 'sighting.status.atTheVet')],
    },
  },
  {
    id: 'map-empty',
    label: 'Empty (no sightings nearby)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'empty',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-nearby-error',
    label: 'Nearby error',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'failure',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-nearby-error-after-denied',
    label: 'Nearby error after a denied location (Search, Try again and "Locate me" together)',
    group: 'nearbyMap',
    geolocation: 'denied',
    nearby: 'failure',
    leaflet: 'tileless',
    steps: [
      ...repeat(countryToSearchZoomSteps, click(zoomIn)),
      waitForText(status, 'map.hint'),
      press('map.searchArea'),
      waitFor('app-alert[role="alert"]'),
    ],
  },
  {
    id: 'map-load-failed',
    label: 'Map library failed to load',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'items',
    leaflet: 'failure',
    steps: [],
  },
  {
    id: 'map-load-failed-unknown-position',
    label: 'Map library failed to load, position unknown (status and Search suppressed)',
    group: 'nearbyMap',
    geolocation: 'denied',
    nearby: 'items',
    leaflet: 'failure',
    steps: [],
  },
];

const detailMap = '#sighting-detail-map app-map-view';
const commentForm = 'form.sighting-comments__add';
const commentField = `${commentForm} textarea`;
const contactButton = 'button.sighting-detail__contact';
const messageField = 'textarea';
const openMessageDialog = (...steps: CellStep[]): CellScenario => ({
  action: 'Open dialog',
  trigger: click(contactButton),
  steps: [waitFor(messageField), ...steps],
});

const detailCells: readonly SightingDetailCell[] = [
  {
    id: 'detail-loaded',
    label: 'Loaded (default)',
    group: 'sightingDetail',
    detail: 'loaded',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-unnamed',
    label: 'Loaded, unnamed sighting',
    group: 'sightingDetail',
    detail: 'loadedUnnamed',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-long-name',
    label: 'Loaded, long name (heading wraps)',
    group: 'sightingDetail',
    detail: 'loadedLongName',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-map-shown',
    label: 'Map shown ("Show map" pressed)',
    group: 'sightingDetail',
    detail: 'loaded',
    leaflet: 'tileless',
    steps: [press('sighting.detail.showMap'), waitFor(detailMap)],
  },
  {
    id: 'detail-not-found',
    label: 'Not found (404)',
    group: 'sightingDetail',
    detail: 'notFound',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-error',
    label: 'Error (500, Try again)',
    group: 'sightingDetail',
    detail: 'failure',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-loading',
    label: 'Loading (request never completes)',
    group: 'sightingDetail',
    detail: 'pending',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-error-then-loaded',
    label: 'Error, then loaded (Try again runs from the header, focus moves to the heading)',
    group: 'sightingDetail',
    detail: 'failureThenLoaded',
    leaflet: 'tileless',
    steps: [],
    manual: {
      action: 'Press Try again',
      steps: [press('sighting.detail.retry'), waitFor('dl')],
    },
  },
  {
    id: 'detail-map-load-failed',
    label: 'Map library failed to load after "Show map"',
    group: 'sightingDetail',
    detail: 'loaded',
    leaflet: 'failure',
    steps: [press('sighting.detail.showMap'), waitFor(detailMap)],
  },
  {
    id: 'detail-photo-loaded',
    label: 'Photo loaded (generated 4:3 image)',
    group: 'sightingDetail',
    detail: 'loadedWithPhoto',
    photo: 'image',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-photo-loading',
    label: 'Photo loading (request never completes, details shown)',
    group: 'sightingDetail',
    detail: 'loadedWithPhoto',
    photo: 'pending',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-photo-error',
    label: 'Photo failed to load (500, inline message)',
    group: 'sightingDetail',
    detail: 'loadedWithPhoto',
    photo: 'failure',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-comments-loading',
    label: 'Comments loading (request never completes, details shown)',
    group: 'sightingDetail',
    detail: 'loaded',
    comments: 'pending',
    leaflet: 'tileless',
    steps: [press('sighting.comments.expand')],
  },
  {
    id: 'detail-comments-empty',
    label: 'Comments empty (muted text, add form)',
    group: 'sightingDetail',
    detail: 'loaded',
    comments: 'empty',
    leaflet: 'tileless',
    steps: [press('sighting.comments.expand')],
  },
  {
    id: 'detail-comments-collapsed',
    label: 'Comments collapsed by default (heading with count and "Show comments")',
    group: 'sightingDetail',
    detail: 'loaded',
    comments: 'items',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-comments-list',
    label: 'Comments: own, other, edited, deleted, with "Show older comments" in the box',
    group: 'sightingDetail',
    detail: 'loaded',
    comments: 'items',
    leaflet: 'tileless',
    steps: [press('sighting.comments.expand')],
  },
  {
    id: 'detail-comments-error',
    label: 'Comments failed to load (500, Try again)',
    group: 'sightingDetail',
    detail: 'loaded',
    comments: 'failure',
    leaflet: 'tileless',
    steps: [press('sighting.comments.expand')],
  },
  {
    id: 'detail-comments-cooldown',
    label: 'Comment posted within the cooldown (alert with seconds, text kept)',
    group: 'sightingDetail',
    detail: 'loaded',
    comments: 'items',
    addComment: 'cooldown',
    leaflet: 'tileless',
    steps: [
      press('sighting.comments.expand'),
      fill(commentField, ['Spotted it again near the fountain.']),
      submitForm(),
      waitFor(`${commentForm} app-alert`),
    ],
  },
  {
    id: 'detail-comments-editing',
    label: 'Editing an own comment (Edit runs from the header, focus moves to the field)',
    group: 'sightingDetail',
    detail: 'loaded',
    comments: 'items',
    leaflet: 'tileless',
    steps: [press('sighting.comments.expand')],
    manual: {
      action: 'Press Edit',
      steps: [press('sighting.comments.edit'), waitFor('.sighting-comments__edit textarea')],
    },
  },
  {
    id: 'detail-message-own',
    label: 'Own sighting (no "Message {owner}" button)',
    group: 'sightingDetail',
    detail: 'loadedOwn',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-message-existing',
    label: 'Existing conversation (the email icon links to it instead of opening the dialog)',
    group: 'sightingDetail',
    detail: 'loaded',
    ownThread: 'existing',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'detail-message-dialog-open',
    label: 'Message dialog, open (empty field)',
    group: 'sightingDetail',
    detail: 'loaded',
    leaflet: 'tileless',
    steps: [],
    dialog: openMessageDialog(),
  },
  {
    id: 'detail-message-dialog-validation',
    label: 'Message dialog, validation error (whitespace only)',
    group: 'sightingDetail',
    detail: 'loaded',
    leaflet: 'tileless',
    steps: [],
    dialog: openMessageDialog(fill(messageField, ['   ']), submitForm(), waitFor(fieldError)),
  },
  {
    id: 'detail-message-dialog-sending',
    label: 'Message dialog, sending (request never completes)',
    group: 'sightingDetail',
    detail: 'loaded',
    startThread: 'pending',
    leaflet: 'tileless',
    steps: [],
    dialog: openMessageDialog(
      fill(messageField, ['Is it still under the bench?']),
      submitForm(),
      waitFor(busyButton),
    ),
  },
  {
    id: 'detail-message-dialog-failure',
    label: 'Message dialog, send failed (500 with a reference)',
    group: 'sightingDetail',
    detail: 'loaded',
    startThread: 'failure',
    leaflet: 'tileless',
    steps: [],
    dialog: openMessageDialog(
      fill(messageField, ['Is it still under the bench?']),
      submitForm(),
      waitFor('app-alert'),
    ),
  },
];

const listRow = 'li[app-sighting-row]';

const listCells: readonly SightingListCell[] = [
  {
    id: 'list-locating',
    label: 'Locating (geolocation never answers)',
    group: 'sightingList',
    geolocation: 'pending',
    list: 'items',
    steps: [],
  },
  {
    id: 'list-location-denied',
    label: 'Location denied (message and map link, no request)',
    group: 'sightingList',
    geolocation: 'denied',
    list: 'items',
    steps: [],
  },
  {
    id: 'list-loading',
    label: 'Loading (list request never completes)',
    group: 'sightingList',
    geolocation: 'ok',
    list: 'pending',
    steps: [],
  },
  {
    id: 'list-loaded',
    label: 'Loaded (mixed criticalities and ages, one page)',
    group: 'sightingList',
    geolocation: 'ok',
    list: 'items',
    steps: [],
  },
  {
    id: 'list-thumbnails-loading',
    label: 'Thumbnails loading (camera placeholder in photo rows)',
    group: 'sightingList',
    geolocation: 'ok',
    list: 'items',
    thumbnail: 'pending',
    steps: [],
  },
  {
    id: 'list-thumbnails-error',
    label: 'Thumbnails failed (500, camera icon stays)',
    group: 'sightingList',
    geolocation: 'ok',
    list: 'items',
    thumbnail: 'failure',
    steps: [],
  },
  {
    id: 'list-second-page',
    label: 'Second page (13 sightings, Next page pressed)',
    group: 'sightingList',
    geolocation: 'ok',
    list: 'paged',
    steps: [waitFor(listRow), pressLabelled('list.paginator.nextPage')],
  },
  {
    id: 'list-empty',
    label: 'Empty (no sightings nearby)',
    group: 'sightingList',
    geolocation: 'ok',
    list: 'empty',
    steps: [],
  },
  {
    id: 'list-filtered-no-match',
    label: 'Filter with no match (Adopted chosen from the cell header)',
    group: 'sightingList',
    geolocation: 'ok',
    list: 'items',
    steps: [],
    manual: {
      action: 'Choose Adopted',
      steps: [choose(statusFilter, 'sighting.status.adopted')],
    },
  },
  {
    id: 'list-error',
    label: 'Error (500, Try again)',
    group: 'sightingList',
    geolocation: 'ok',
    list: 'failure',
    steps: [],
  },
];

const messagesCells: readonly MessagesCell[] = [
  {
    id: 'messages-loading',
    label: 'Loading (threads request never completes)',
    group: 'messages',
    inbox: 'pending',
    steps: [],
  },
  {
    id: 'messages-empty',
    label: 'Empty (no conversations yet)',
    group: 'messages',
    inbox: 'empty',
    steps: [],
  },
  {
    id: 'messages-error',
    label: 'Threads failed to load (500, Try again)',
    group: 'messages',
    inbox: 'failure',
    steps: [],
  },
  {
    id: 'messages-list',
    label:
      'Threads, none chosen (three unread rows, prompt in the pane, "Show more" in the sidebar)',
    group: 'messages',
    inbox: 'items',
    steps: [],
  },
  {
    id: 'messages-thread',
    label:
      'Thread chosen (bubbles, own on the right, "Show older messages" at the top, its row marked read)',
    group: 'messages',
    inbox: 'items',
    selected: true,
    steps: [],
  },
  {
    id: 'messages-thread-loading',
    label: 'Thread chosen, messages loading (request never completes)',
    group: 'messages',
    inbox: 'items',
    selected: true,
    thread: 'pending',
    steps: [],
  },
  {
    id: 'messages-thread-error',
    label: 'Thread chosen, messages failed to load (500, Try again)',
    group: 'messages',
    inbox: 'items',
    selected: true,
    thread: 'failure',
    steps: [],
  },
  {
    id: 'messages-thread-fullscreen',
    label: 'Thread in the full screen popup (opened from the header, focus moves to the popup)',
    group: 'messages',
    inbox: 'items',
    selected: true,
    steps: [],
    dialog: {
      action: 'Open full screen',
      trigger: click('button.message-thread__expand'),
      steps: [waitFor('li.message-thread__item')],
    },
  },
];

const messagesPhoneCells: readonly MessagesCell[] = [
  {
    id: 'messages-phone-list',
    label: 'Narrow, none chosen (threads only)',
    group: 'messages',
    inbox: 'items',
    steps: [],
  },
  {
    id: 'messages-phone-thread',
    label: 'Narrow, thread chosen (conversation with "All conversations", threads hidden)',
    group: 'messages',
    inbox: 'items',
    selected: true,
    steps: [],
  },
];

const headerCells: readonly HeaderCell[] = [
  {
    id: 'header-messages-read',
    label: 'Messages button, nothing unread (no badge)',
    group: 'header',
    unreadCount: 0,
    steps: [],
  },
  {
    id: 'header-messages-unread',
    label: 'Messages button with 3 unread messages',
    group: 'header',
    unreadCount: 3,
    steps: [],
  },
  {
    id: 'header-messages-unread-many',
    label: 'Messages button with 128 unread messages',
    group: 'header',
    unreadCount: 128,
    steps: [],
  },
];

export const cellGroups: readonly CellGroup[] = [
  { title: 'Profile', wide: false, cells: [...profileCells, ...dialogCells] },
  { title: 'Nearby map', wide: true, cells: nearbyMapCells },
  { title: 'Sighting detail', wide: true, cells: detailCells },
  { title: 'Sighting list', wide: false, cells: listCells },
  { title: 'Messages', wide: true, cells: messagesCells },
  { title: 'Messages, narrow', wide: false, cells: messagesPhoneCells },
  { title: 'Header', wide: false, cells: headerCells },
];
