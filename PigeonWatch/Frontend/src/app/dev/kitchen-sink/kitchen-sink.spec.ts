import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { cellGroups } from './cell-definitions';
import { KitchenSink } from './kitchen-sink';

const patience = { timeout: 5000, interval: 25 };

describe('KitchenSink', () => {
  let fixture: ComponentFixture<KitchenSink>;
  let root: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(KitchenSink);
    root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  function cell(id: string): HTMLElement {
    const element = root.querySelector<HTMLElement>(`[data-cell="${id}"]`);

    if (!element) throw new Error(`No cell ${id}`);

    return element;
  }

  function dialog(): HTMLElement | null {
    return document.querySelector<HTMLElement>('app-password-dialog');
  }

  async function eventually(check: () => void): Promise<void> {
    await vi.waitFor(() => {
      fixture.detectChanges();
      check();
    }, patience);
  }

  async function openDialogOf(id: string): Promise<void> {
    cell(id).querySelector<HTMLButtonElement>('header button')?.click();
    await eventually(() => expect(dialog()).not.toBeNull());
  }

  function textOf(element: Element | null | undefined): string {
    return element?.textContent?.trim() ?? '';
  }

  it('hides the global warm-up panel while it is shown and restores it on destroy', () => {
    const panel = document.createElement('app-warmup-panel');
    document.body.appendChild(panel);

    const sink = TestBed.createComponent(KitchenSink);

    expect(panel.hidden).toBe(true);

    sink.destroy();

    expect(panel.hidden).toBe(false);
    panel.remove();
  });

  it('renders every cell once with its label', () => {
    const definitions = cellGroups.flatMap((group) => group.cells);

    expect(definitions.length).toBe(13);

    for (const definition of definitions) {
      expect(textOf(cell(definition.id).querySelector('h3'))).toBe(definition.label);
      expect(cell(definition.id).querySelector('app-profile-page')).not.toBeNull();
    }
  });

  it('shows the read-only state', () => {
    const element = cell('read-only');

    expect(element.querySelector('input')).toBeNull();
    expect(element.textContent).toContain('jan@example.com');
    expect(element.textContent).toContain('Jan K');
  });

  it('shows email and name as description list rows with Change password in the footer', () => {
    const element = cell('read-only');

    expect(element.querySelectorAll('dl > div > dt').length).toBe(2);
    expect(element.querySelectorAll('dl > div > dd').length).toBe(2);
    expect(element.querySelectorAll('mat-card-actions button').length).toBe(1);
  });

  it('shows the pristine edit state with Save disabled', async () => {
    await eventually(() => {
      const save = cell('edit-pristine').querySelector<HTMLButtonElement>(
        'app-submit-button button',
      );

      const cancel = cell('edit-pristine').querySelector<HTMLButtonElement>(
        'app-form-actions button[type="button"]',
      );

      expect(save).not.toBeNull();
      expect(save?.disabled).toBe(true);
      expect(cancel?.disabled).toBe(false);
      expect(cell('edit-pristine').querySelector('mat-card-actions button')).toBeNull();
    });
  });

  it('shows a client validation error in the name field', async () => {
    await eventually(() => {
      expect(textOf(cell('edit-client-error').querySelector('mat-form-field mat-error'))).not.toBe(
        '',
      );
    });
  });

  it('shows the DuplicateDisplayName server error in the name field', async () => {
    await eventually(() => {
      expect(
        textOf(cell('edit-duplicate-name').querySelector('mat-form-field mat-error')),
      ).not.toBe('');
    });
  });

  it('shows the form-level server error as an alert', async () => {
    await eventually(() => {
      expect(textOf(cell('edit-form-error').querySelector('app-alert[role="alert"]'))).not.toBe('');
    });

    const element = cell('edit-form-error');
    const alert = element.querySelector('form app-alert[role="alert"]');
    const actions = element.querySelector('form app-form-actions');

    expect(alert).not.toBeNull();
    expect(
      alert!.compareDocumentPosition(actions!) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(element.querySelectorAll('app-alert').length).toBe(1);
  });

  it('shows the saving state with a spinner in the submit button', async () => {
    await eventually(() => {
      expect(
        cell('saving').querySelector('app-submit-button [aria-busy="true"] mat-progress-spinner'),
      ).not.toBeNull();
      expect(
        cell('saving').querySelector<HTMLButtonElement>('app-form-actions button[type="button"]')
          ?.disabled,
      ).toBe(true);
    });
  });

  it('shows the saved notice and the new name', async () => {
    await eventually(() => {
      expect(textOf(cell('saved').querySelector('app-alert[role="status"]'))).not.toBe('');
      expect(cell('saved').textContent).toContain('Jan Kowalski');
    });
  });

  it('shows the password-changed notice after the stubbed dialog completes', async () => {
    await openDialogOf('password-changed');

    await eventually(() => {
      expect(textOf(cell('password-changed').querySelector('app-alert[role="status"]'))).not.toBe(
        '',
      );
    });
  });

  it('shows only the password notice after a name save followed by a password change', async () => {
    const element = cell('saved-then-password-changed');

    await eventually(() =>
      expect(element.querySelectorAll('app-alert[role="status"]').length).toBe(1),
    );

    const savedNotice = textOf(element.querySelector('app-alert'));

    await openDialogOf('saved-then-password-changed');

    await eventually(() => {
      expect(element.querySelectorAll('app-alert').length).toBe(1);
      expect(textOf(element.querySelector('app-alert'))).not.toBe(savedNotice);
    });
  });

  it('opens the dialog with three empty password fields', async () => {
    await openDialogOf('dialog-default');

    await eventually(() => {
      expect(dialog()?.querySelectorAll('input[type="password"]').length).toBe(3);
      expect(dialog()?.querySelector('mat-form-field mat-error')).toBeNull();
    });
  });

  it('opens the dialog with field errors', async () => {
    await openDialogOf('dialog-field-errors');

    await eventually(() => {
      expect(textOf(dialog()?.querySelector('mat-form-field mat-error'))).not.toBe('');
    });
  });

  it('opens the dialog with the wrong current password error and never calls the API', async () => {
    await openDialogOf('dialog-wrong-current');

    await eventually(() => {
      const firstField = dialog()?.querySelector('mat-form-field');

      expect(textOf(firstField?.querySelector('mat-error'))).not.toBe('');
    });
    TestBed.inject(HttpTestingController).verify();
  });

  it('opens the dialog in the busy state with a spinner', async () => {
    await openDialogOf('dialog-busy');

    await eventually(() => {
      expect(
        dialog()?.querySelector('app-submit-button [aria-busy="true"] mat-progress-spinner'),
      ).not.toBeNull();
      expect(
        dialog()?.querySelector<HTMLButtonElement>('app-form-actions button[type="button"]')
          ?.disabled,
      ).toBe(true);
    });
  });
});
