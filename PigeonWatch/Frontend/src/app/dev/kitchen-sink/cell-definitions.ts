import { CellStep, click, fill, press, submitForm, waitFor } from './cell-driver';
import { NearbyMapModes, ProfileModes, StubModes } from './cell-stubs';

export interface DialogScenario {
  readonly action: string;
  readonly steps: readonly CellStep[];
}

interface CellBase {
  readonly id: string;
  readonly label: string;
  readonly steps: readonly CellStep[];
  readonly dialog?: DialogScenario;
}

export type ProfileCell = CellBase & ProfileModes;
export type NearbyMapCell = CellBase & NearbyMapModes;
export type CellDefinition = CellBase & StubModes;

export interface CellGroup {
  readonly title: string;
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
    id: 'map-position-unknown',
    label: 'Position unknown (location denied)',
    group: 'nearbyMap',
    geolocation: 'denied',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-loading',
    label: 'Loading (nearby request never completes)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'pending',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-loaded',
    label: 'Loaded with markers',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [],
  },
  {
    id: 'map-selected',
    label: 'Selected (marker clicked)',
    group: 'nearbyMap',
    geolocation: 'ok',
    nearby: 'items',
    leaflet: 'tileless',
    steps: [click(marker), waitFor('mat-card')],
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
  { title: 'Profile', cells: [...profileCells, ...dialogCells] },
  { title: 'Nearby map', cells: nearbyMapCells },
];
