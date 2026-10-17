import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, DebugElement, EnvironmentInjector, createComponent } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ResourceService } from '../../core/resources/resource.service';
import { NearbyMapPage } from '../../features/sightings/nearby-map-page/nearby-map-page';
import { LEAFLET_LOADER, MapView } from '../../shared/map/map-view/map-view';
import { MapViewStub } from '../../shared/map/testing/map-view-stub';
import { CellDefinition, cellGroups } from './cell-definitions';
import { sinkPosition, sinkSightings } from './cell-stubs';
import { KitchenSink } from './kitchen-sink';

const patience = { timeout: 2000, interval: 25 };

describe('KitchenSink', () => {
  let fixture: ComponentFixture<KitchenSink>;
  let root: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.overrideComponent(NearbyMapPage, {
      remove: { imports: [MapView] },
      add: { imports: [MapViewStub] },
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

  function cellDebug(id: string): DebugElement {
    return fixture.debugElement.query(By.css(`[data-cell="${id}"]`));
  }

  function mapOf(id: string): MapViewStub {
    return cellDebug(id).query(By.directive(MapViewStub)).componentInstance as MapViewStub;
  }

  function label(key: string, ...args: unknown[]): string {
    return TestBed.inject(ResourceService).translate(key, ...args);
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

  function buttonOf(id: string, key: string): HTMLButtonElement | null {
    return (
      [...cell(id).querySelectorAll<HTMLButtonElement>('button')].find(
        (button) => textOf(button) === label(key),
      ) ?? null
    );
  }

  function statusOf(id: string): string {
    return textOf(cell(id).querySelector('[role="status"]'));
  }

  function chooseNextLocation(id: string, mode: string): void {
    const select = cell(id).querySelector<HTMLSelectElement>('header select');

    if (!select) throw new Error(`No next-location control in ${id}`);

    select.value = mode;
    select.dispatchEvent(new Event('change'));
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

  it('renders every cell once with its label and the page of its group', () => {
    const definitions = cellGroups.flatMap((group) => group.cells);
    const pageOf: Record<CellDefinition['group'], string> = {
      profile: 'app-profile-page',
      nearbyMap: 'app-nearby-map-page',
    };

    expect(root.querySelectorAll('app-state-cell').length).toBe(definitions.length);
    expect(new Set(definitions.map((definition) => definition.id)).size).toBe(definitions.length);

    for (const definition of definitions) {
      const element = cell(definition.id);

      expect(textOf(element.querySelector('h3'))).toBe(definition.label);
      expect(element.querySelectorAll('app-profile-page, app-nearby-map-page')).toHaveLength(1);
      expect(element.querySelector(pageOf[definition.group])).not.toBeNull();
    }
  });

  it('heads the page and each view group', () => {
    expect(textOf(root.querySelector('h1'))).toBe('Kitchen sink');
    expect([...root.querySelectorAll('.sink__group > h2')].map(textOf)).toEqual(
      cellGroups.map((group) => group.title),
    );
  });

  it('shows the locating map state', async () => {
    await eventually(() => {
      expect(textOf(cell('map-locating').querySelector('[role="status"]'))).toBe(
        label('map.locating'),
      );
      expect(cell('map-locating').querySelector('app-alert')).toBeNull();
    });
  });

  it('asks to zoom in and disables Search when the position is unknown at the country view', async () => {
    await eventually(() => {
      expect(statusOf('map-zoomed-out')).toBe(label('map.zoomToSearch'));
      expect(textOf(cell('map-zoomed-out').querySelector('h1'))).toBe(label('map.titleArea'));
      expect(mapOf('map-zoomed-out').markers()).toEqual([]);
      expect(buttonOf('map-zoomed-out', 'map.searchArea')?.getAttribute('aria-disabled')).toBe(
        'true',
      );
      expect(buttonOf('map-zoomed-out', 'map.locateMe')).not.toBeNull();
    });
  });

  it('shows the hint and enables Search once an unknown position is zoomed to level 10', async () => {
    mapOf('map-hint').zoomChange.emit(10);

    await eventually(() => {
      expect(statusOf('map-hint')).toBe(label('map.hint'));
      expect(buttonOf('map-hint', 'map.searchArea')?.hasAttribute('aria-disabled')).toBe(false);
    });
  });

  it('keeps "Locate me" and the zoom reason when the next answer times out again', async () => {
    await eventually(() => expect(buttonOf('map-use-my-location', 'map.locateMe')).not.toBeNull());

    chooseNextLocation('map-use-my-location', 'timeout');
    buttonOf('map-use-my-location', 'map.locateMe')?.click();

    await eventually(() => {
      expect(statusOf('map-use-my-location')).toBe(label('map.zoomToSearch'));
      expect(buttonOf('map-use-my-location', 'map.locateMe')?.disabled).toBe(false);
      expect(textOf(cell('map-use-my-location').querySelector('h1'))).toBe(label('map.titleArea'));
    });
  });

  it('recentres and searches when the next "Locate me" answer finds the position', async () => {
    await eventually(() => expect(buttonOf('map-use-my-location', 'map.locateMe')).not.toBeNull());

    chooseNextLocation('map-use-my-location', 'ok');
    buttonOf('map-use-my-location', 'map.locateMe')?.click();

    await eventually(() => {
      expect(textOf(cell('map-use-my-location').querySelector('h1'))).toBe(label('map.title'));
      expect(mapOf('map-use-my-location').center()).toEqual(sinkPosition);
      expect(mapOf('map-use-my-location').markers()).toHaveLength(sinkSightings.length);
      expect(buttonOf('map-use-my-location', 'map.locateMe')).toBeNull();
    });
  });

  it('disables "Locate me" while a repeated location request is pending', async () => {
    await eventually(() => {
      expect(statusOf('map-retry-locating')).toBe(label('map.locating'));
      expect(buttonOf('map-retry-locating', 'map.locateMe')?.getAttribute('aria-disabled')).toBe(
        'true',
      );
      expect(buttonOf('map-retry-locating', 'map.searchArea')?.getAttribute('aria-disabled')).toBe(
        'true',
      );
    });
  });

  it('shows the spinner while the nearby query is pending', async () => {
    await eventually(() => {
      expect(
        cell('map-loading').querySelector('[role="status"] mat-progress-spinner'),
      ).not.toBeNull();
      expect(mapOf('map-loading').center()).toEqual(sinkPosition);
    });
  });

  it('keeps the previous markers and disables Search while a repeated search loads', async () => {
    await eventually(() => {
      expect(
        cell('map-loading-kept').querySelector('[role="status"] app-loading-indicator'),
      ).not.toBeNull();
      expect(mapOf('map-loading-kept').markers()).toHaveLength(sinkSightings.length);
      expect(buttonOf('map-loading-kept', 'map.searchArea')?.getAttribute('aria-disabled')).toBe(
        'true',
      );
    });
  });

  it('shows a marker for every fixture sighting', async () => {
    await eventually(() => {
      const markers = mapOf('map-loaded').markers();

      expect(markers.map((marker) => marker.id)).toEqual(
        sinkSightings.map((sighting) => sighting.id),
      );
      expect(new Set(markers.map((marker) => marker.kind))).toEqual(
        new Set(['monitor', 'needsHelpSoon', 'urgent']),
      );
      expect(markers.map((marker) => marker.label)).toContain(
        label('map.markerLabel', label('sighting.unnamed'), label('sighting.criticality.monitor')),
      );
    });
  });

  it('shows the card of the selected marker', async () => {
    await eventually(() => expect(mapOf('map-selected').markers()).toHaveLength(4));

    mapOf('map-selected').markerSelect.emit('sink-urgent');

    await eventually(() => {
      expect(textOf(cell('map-selected').querySelector('mat-card h2'))).toBe('Limpy');
      expect(mapOf('map-selected').selectedId()).toBe('sink-urgent');
    });
  });

  it('offers the selection as a header action so no cell takes focus on load', () => {
    for (const id of ['map-selected', 'map-selected-keyboard']) {
      const action = cell(id).querySelector('header button');

      expect(textOf(action)).not.toBe('');
      expect(cell(id).querySelector('mat-card')).toBeNull();
    }
  });

  it('shows the card of the marker selected with the keyboard', async () => {
    await eventually(() => expect(mapOf('map-selected-keyboard').markers()).toHaveLength(4));

    mapOf('map-selected-keyboard').markerSelect.emit('sink-needs-help-soon');

    await eventually(() => {
      expect(textOf(cell('map-selected-keyboard').querySelector('mat-card h2'))).toBe('Limpy');
      expect(mapOf('map-selected-keyboard').selectedId()).toBe('sink-needs-help-soon');
    });
  });

  it('shows the empty text when nothing is nearby', async () => {
    await eventually(() => {
      expect(textOf(cell('map-empty').querySelector('[role="status"]'))).toBe(
        label('map.empty', 10),
      );
    });
  });

  it('shows the nearby error as an alert', async () => {
    await eventually(() => {
      expect(textOf(cell('map-nearby-error').querySelector('app-alert[role="alert"]'))).toBe(
        label('map.error'),
      );
    });
  });

  it('shows Search, Try again and Locate me together with the alert in the slot after a denied location', async () => {
    const id = 'map-nearby-error-after-denied';

    mapOf(id).zoomChange.emit(10);
    await eventually(() => expect(statusOf(id)).toBe(label('map.hint')));

    buttonOf(id, 'map.searchArea')?.click();

    await eventually(() => {
      expect(textOf(cell(id).querySelector('app-alert[role="alert"]'))).toBe(label('map.error'));
    });

    const slot = cell(id).querySelector('.nearby-toolbar__slot');

    expect(buttonOf(id, 'map.searchArea')).not.toBeNull();
    expect(buttonOf(id, 'map.retry')).not.toBeNull();
    expect(buttonOf(id, 'map.locateMe')).not.toBeNull();
    expect(slot?.contains(cell(id).querySelector('app-alert[role="alert"]'))).toBe(true);
    expect(cell(id).querySelectorAll('[role="status"]')).toHaveLength(1);
  });

  it('gives the map-library cell a loader that fails', async () => {
    const loader = cellDebug('map-load-failed').injector.get(LEAFLET_LOADER);

    await expect(firstValueFrom(loader())).rejects.toThrow();
  });

  it('gives the failed-map cell with an unknown position a loader that fails', async () => {
    const loader = cellDebug('map-load-failed-unknown-position').injector.get(LEAFLET_LOADER);

    await expect(firstValueFrom(loader())).rejects.toThrow();
  });

  it('draws focusable markers with no tile images through a map cell loader', async () => {
    const appRef = TestBed.inject(ApplicationRef);
    const host = document.createElement('div');
    document.body.appendChild(host);
    const map = createComponent(MapView, {
      environmentInjector: TestBed.inject(EnvironmentInjector),
      elementInjector: cellDebug('map-loaded').injector,
      hostElement: host,
    });
    appRef.attachView(map.hostView);
    map.setInput('label', 'Map');
    map.setInput('center', sinkPosition);
    map.setInput('zoom', 12);
    map.setInput('markers', [{ ...sinkPosition, id: 'marker', kind: 'urgent', label: 'Limpy' }]);

    try {
      await vi.waitFor(() => {
        appRef.tick();
        expect(host.querySelector('.pw-map-marker--urgent')?.getAttribute('tabindex')).toBe('0');
      }, patience);

      expect(host.querySelector('.leaflet-tile-pane')).not.toBeNull();
      expect(host.querySelector('img')).toBeNull();
    } finally {
      map.destroy();
      host.remove();
    }
  });

  it('shows the read-only state', () => {
    const element = cell('read-only');

    expect(element.querySelector('input')).toBeNull();
    expect(element.textContent).toContain('jan@example.com');
    expect(element.textContent).toContain('Jan K');
  });

  it('shows My sightings items with a criticality badge in each meta line', async () => {
    await eventually(() => {
      const items = cell('read-only-my-sightings').querySelectorAll('app-my-sightings li');

      expect(items).toHaveLength(sinkSightings.length);
      items.forEach((item) => expect(item.querySelector('app-criticality-badge')).not.toBeNull());
    });
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
