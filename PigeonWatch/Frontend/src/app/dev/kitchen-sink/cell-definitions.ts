import {
  CellStep,
  click,
  fill,
  keydown,
  press,
  repeat,
  submitForm,
  waitFor,
  waitForText,
} from './cell-driver';
import { NearbyMapModes, ProfileModes, StubModes } from './cell-stubs';

export interface CellScenario {
  readonly action: string;
  readonly steps: readonly CellStep[];
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
    label: 'Location timed out ("Use my location", next answer chosen in the header)',
    group: 'nearbyMap',
    geolocation: 'timeoutThenNext',
    nextLocation: 'ok',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-retry-locating',
    label: 'Retrying location ("Use my location" disabled while locating)',
    group: 'nearbyMap',
    geolocation: 'timeoutThenNext',
    nextLocation: 'pending',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [press('map.useMyLocation'), waitForText(status, 'map.locating')],
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
    id: 'map-load-failed',
    label: 'Map library failed to load',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'items',
    leaflet: 'failure',
    steps: [],
  },
];

export const cellGroups: readonly CellGroup[] = [
  { title: 'Profile', wide: false, cells: [...profileCells, ...dialogCells] },
  { title: 'Nearby map', wide: true, cells: nearbyMapCells },
];
