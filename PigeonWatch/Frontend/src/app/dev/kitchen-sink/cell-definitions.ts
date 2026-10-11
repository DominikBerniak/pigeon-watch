import { CellStep, fill, press, submitForm, waitFor } from './cell-driver';
import { StubModes } from './cell-stubs';

export interface DialogScenario {
  readonly action: string;
  readonly steps: readonly CellStep[];
}

export interface CellDefinition extends StubModes {
  readonly id: string;
  readonly label: string;
  readonly steps: readonly CellStep[];
  readonly dialog?: DialogScenario;
}

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

const profileCells: readonly CellDefinition[] = [
  { id: 'read-only', label: 'Read-only', save: 'success', password: 'success', steps: [] },
  {
    id: 'edit-pristine',
    label: 'Edit, pristine (Save disabled)',
    save: 'success',
    password: 'success',
    steps: [press('profile.edit'), waitFor(nameField)],
  },
  {
    id: 'edit-client-error',
    label: 'Edit, client validation error',
    save: 'success',
    password: 'success',
    steps: submittedName('ab', waitFor(fieldError)),
  },
  {
    id: 'edit-duplicate-name',
    label: 'Edit, server error (DuplicateDisplayName)',
    save: 'duplicate',
    password: 'success',
    steps: submittedName('Taken', waitFor(fieldError)),
  },
  {
    id: 'edit-form-error',
    label: 'Edit, server error (form-level)',
    save: 'failure',
    password: 'success',
    steps: submittedName('Valid name', waitFor('app-alert[role="alert"]')),
  },
  {
    id: 'saving',
    label: 'Saving (request never completes)',
    save: 'pending',
    password: 'success',
    steps: submittedName('Valid name', waitFor(busyButton)),
  },
  {
    id: 'saved',
    label: 'Saved',
    save: 'success',
    password: 'success',
    steps: submittedName('Jan Kowalski', waitFor('app-alert[role="status"]')),
  },
  {
    id: 'password-changed',
    label: 'Password changed',
    save: 'success',
    password: 'success',
    steps: [],
    dialog: {
      action: 'Run password change',
      steps: [fill(passwordFields, validPasswords), submitForm()],
    },
  },
];

const dialogCells: readonly CellDefinition[] = [
  {
    id: 'dialog-default',
    label: 'Dialog, default',
    save: 'success',
    password: 'success',
    steps: [],
    dialog: { action: 'Open dialog', steps: [waitFor(passwordFields)] },
  },
  {
    id: 'dialog-field-errors',
    label: 'Dialog, field errors',
    save: 'success',
    password: 'success',
    steps: [],
    dialog: { action: 'Open dialog', steps: [submitForm(), waitFor(fieldError)] },
  },
  {
    id: 'dialog-wrong-current',
    label: 'Dialog, wrong current password',
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
    save: 'success',
    password: 'pending',
    steps: [],
    dialog: {
      action: 'Open dialog',
      steps: [fill(passwordFields, validPasswords), submitForm(), waitFor(busyButton)],
    },
  },
];

export const cellGroups: readonly CellGroup[] = [
  { title: 'Profile page', cells: profileCells },
  { title: 'Password dialog', cells: dialogCells },
];
