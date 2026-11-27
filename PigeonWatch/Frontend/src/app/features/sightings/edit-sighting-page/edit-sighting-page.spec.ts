import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { NEVER, Observable, Subject, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../core/error-reporting/error-reporting.service';
import { PhotoEncoder } from '../shared/photo-encoder';
import {
  Sighting,
  SightingCriticality,
  SightingStatus,
  SightingUpdate,
} from '../sighting.types';
import { SightingsApi } from '../sightings-api';
import { EditSightingPage } from './edit-sighting-page';

const owned: Sighting = {
  id: 'sighting-1',
  name: 'Limpy',
  description: 'Grey pigeon with a ruffled wing',
  behaviour: 'Cannot fly',
  latitude: 52.2297,
  longitude: 21.0122,
  status: SightingStatus.spotted,
  criticality: SightingCriticality.needsHelpSoon,
  reporterDisplayName: 'Jan K',
  createdAt: '2026-10-11T12:00:00Z',
  hasPhoto: false,
  removedAt: null,
  isMine: true,
  editPermissions: {
    canEditDescription: true,
    allowedNextStatuses: [SightingStatus.caught, SightingStatus.notFound],
  },
};

const locked: Sighting = {
  ...owned,
  editPermissions: {
    canEditDescription: false,
    allowedNextStatuses: [SightingStatus.caught, SightingStatus.notFound],
  },
};

const withPhoto: Sighting = { ...owned, hasPhoto: true };
const notOwned: Sighting = { ...owned, isMine: false, editPermissions: null };
const thumbnailBlob = new Blob(['jpeg'], { type: 'image/jpeg' });
const encodedPhoto = new Blob(['encoded-jpeg'], { type: 'image/jpeg' });
const traceId = '00-abc123-def456-01';

@Component({ template: '' })
class DetailRouteStub {}

function badRequest(code: string): HttpErrorResponse {
  return new HttpErrorResponse({
    status: 400,
    error: { status: 400, traceId, errors: { [code]: ['x'] } },
  });
}

describe('EditSightingPage', () => {
  let get: ReturnType<typeof vi.fn<(id: string) => Observable<Sighting>>>;
  let update: ReturnType<typeof vi.fn<(id: string, update: SightingUpdate) => Observable<Sighting>>>;
  let photoThumbnail: ReturnType<typeof vi.fn<(id: string) => Observable<Blob>>>;
  let revokePreviewUrl: ReturnType<typeof vi.fn<(url: string) => void>>;
  let report: ReturnType<typeof vi.fn>;
  let harness: RouterTestingHarness;
  let loader: HarnessLoader;

  beforeEach(async () => {
    let previews = 0;

    get = vi.fn<(id: string) => Observable<Sighting>>(() => of(owned));
    update = vi.fn<(id: string, update: SightingUpdate) => Observable<Sighting>>(() => of(owned));
    photoThumbnail = vi.fn<(id: string) => Observable<Blob>>(() => of(thumbnailBlob));
    revokePreviewUrl = vi.fn<(url: string) => void>();
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'sightings/:id/edit', component: EditSightingPage },
          { path: 'sightings/:id', component: DetailRouteStub },
        ]),
        { provide: SightingsApi, useValue: { get, update, photoThumbnail } },
        {
          provide: PhotoEncoder,
          useValue: {
            encode: (): Observable<Blob> => of(encodedPhoto),
            createPreviewUrl: (): string => `blob:preview-${++previews}`,
            revokePreviewUrl,
          },
        },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function open(id = 'sighting-1'): Promise<HTMLElement> {
    await harness.navigateByUrl(`/sightings/${id}/edit`);
    await settle();
    loader = TestbedHarnessEnvironment.loader(harness.fixture);

    return harness.routeNativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
  }

  function root(): HTMLElement {
    return harness.routeNativeElement as HTMLElement;
  }

  function nameInput(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('input[type="text"]')!;
  }

  function textareas(): HTMLTextAreaElement[] {
    return [...root().querySelectorAll<HTMLTextAreaElement>('textarea')];
  }

  function fields(): HTMLElement[] {
    return [...root().querySelectorAll<HTMLElement>('mat-form-field')];
  }

  function hintOf(field: HTMLElement): string | undefined {
    return field.querySelector('mat-hint')?.textContent?.trim();
  }

  function setValue(element: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('change'));
    element.dispatchEvent(new Event('blur'));
  }

  function button(text: string): HTMLButtonElement | null {
    return (
      [...root().querySelectorAll<HTMLButtonElement>('button')].find(
        (candidate) => candidate.textContent?.trim() === text,
      ) ?? null
    );
  }

  async function selects(): Promise<MatSelectHarness[]> {
    return loader.getAllHarnesses(MatSelectHarness);
  }

  async function optionLabels(select: MatSelectHarness): Promise<string[]> {
    await select.open();

    const labels = await Promise.all((await select.getOptions()).map((option) => option.getText()));

    await select.close();

    return labels;
  }

  async function choose(select: MatSelectHarness, label: string): Promise<void> {
    await select.open();
    await select.clickOptions({ text: label });
  }

  async function submit(): Promise<void> {
    root().querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  async function chooseNewPhoto(): Promise<void> {
    const input = root().querySelector<HTMLInputElement>('app-photo-picker input[type="file"]')!;
    const file = new File(['raw'], 'pigeon.jpg', { type: 'image/jpeg' });

    Object.defineProperty(input, 'files', {
      configurable: true,
      value: { length: 1, item: () => file },
    });
    input.dispatchEvent(new Event('change'));
    await settle();
  }

  function formAlert(): HTMLElement | null {
    return root().querySelector<HTMLElement>('form app-alert');
  }

  function currentThumbnail(): HTMLImageElement | null {
    return root().querySelector<HTMLImageElement>('img.edit-sighting__current-photo-image');
  }

  function updateSent(): SightingUpdate {
    expect(update).toHaveBeenCalledOnce();

    return update.mock.calls[0][1];
  }

  it('loads the sighting and fills the form with its editable values', async () => {
    const page = await open();

    expect(get).toHaveBeenCalledExactlyOnceWith('sighting-1');
    expect(page.querySelector('app-page-card h1')?.textContent?.trim()).toBe('Edit sighting');
    expect(page.querySelector('app-page-card header p')?.textContent?.trim()).toBe('Limpy');
    expect(page.querySelector('app-page-card')?.classList).toContain('page-card-host--wide');
    expect(fields().map((field) => field.querySelector('mat-label')?.textContent?.trim())).toEqual([
      'Name (optional)',
      'Description',
      'Behaviour',
      'Status',
      'Criticality',
    ]);
    expect(nameInput().value).toBe('Limpy');
    expect(textareas().map((textarea) => textarea.value)).toEqual([
      'Grey pigeon with a ruffled wing',
      'Cannot fly',
    ]);

    const [status, criticality] = await selects();

    expect(await status.getValueText()).toBe('Spotted');
    expect(await criticality.getValueText()).toBe('Needs help soon');
  });

  it('shows no location and no map', async () => {
    const page = await open();

    expect(page.querySelector('dl')).toBeNull();
    expect(page.textContent).not.toContain('52.22970');
    expect(page.querySelector('app-map-view')).toBeNull();
    expect(page.querySelector('app-location-picker')).toBeNull();
  });

  it('offers the current status followed by the allowed next statuses', async () => {
    await open();
    const [status] = await selects();

    expect(await optionLabels(status)).toEqual(['Spotted', 'Caught', 'Not found']);
  });

  it('offers only the current status in an end state', async () => {
    get.mockReturnValue(
      of({
        ...owned,
        status: SightingStatus.adopted,
        editPermissions: { canEditDescription: true, allowedNextStatuses: [] },
      }),
    );
    await open();
    const [status] = await selects();

    expect(await optionLabels(status)).toEqual(['Adopted']);
  });

  it('offers the three criticalities', async () => {
    await open();
    const [, criticality] = await selects();

    expect(await optionLabels(criticality)).toEqual(['Monitor', 'Needs help soon', 'Urgent']);
  });

  it('keeps the description editable while the server allows it', async () => {
    await open();
    const [description] = textareas();

    expect(description.disabled).toBe(false);
    expect(hintOf(fields()[1])).toBe('What does the bird look like? Up to 1000 characters.');
  });

  it('disables the description with the lock hint once someone else has engaged', async () => {
    get.mockReturnValue(of(locked));
    await open();
    const [description, behaviour] = textareas();

    expect(description.disabled).toBe(true);
    expect(behaviour.disabled).toBe(false);
    expect(nameInput().disabled).toBe(false);
    expect(hintOf(fields()[1])).toBe(
      "The description can't change any more because someone else has commented on this sighting or contacted you about it.",
    );
  });

  it('saves a status change from a locked form with the stored description', async () => {
    get.mockReturnValue(of(locked));
    await open();
    const [status] = await selects();

    await choose(status, 'Caught');
    await submit();

    expect(updateSent()).toEqual({
      name: 'Limpy',
      description: 'Grey pigeon with a ruffled wing',
      behaviour: 'Cannot fly',
      status: SightingStatus.caught,
      criticality: SightingCriticality.needsHelpSoon,
      photo: null,
      removePhoto: false,
    });
  });

  it('saves the edited fields and opens the detail page', async () => {
    await open();
    setValue(nameInput(), '  Limpy Jr  ');
    setValue(textareas()[0], 'Grey pigeon, left wing ruffled');
    setValue(textareas()[1], 'Walks slowly');
    const [status, criticality] = await selects();

    await choose(status, 'Caught');
    await choose(criticality, 'Urgent');
    await submit();

    expect(update.mock.calls[0][0]).toBe('sighting-1');
    expect(updateSent()).toEqual({
      name: 'Limpy Jr',
      description: 'Grey pigeon, left wing ruffled',
      behaviour: 'Walks slowly',
      status: SightingStatus.caught,
      criticality: SightingCriticality.urgent,
      photo: null,
      removePhoto: false,
    });
    expect(TestBed.inject(Router).url).toBe('/sightings/sighting-1');
  });

  it('sends a blank name as null', async () => {
    await open();
    setValue(nameInput(), '   ');
    await submit();

    expect(updateSent().name).toBeNull();
  });

  it('blocks a whitespace-only behaviour with the required error', async () => {
    await open();
    setValue(textareas()[1], '  ');
    await submit();

    expect(update).not.toHaveBeenCalled();
    expect(fields()[2].querySelector('mat-error')?.textContent?.trim()).toBe(
      'This field is required.',
    );
  });

  it('shows the busy submit button while the save is in flight', async () => {
    update.mockReturnValue(NEVER);
    await open();
    await submit();

    expect(root().querySelector('app-submit-button [aria-busy="true"]')).not.toBeNull();
    expect(button('Cancel')?.disabled).toBe(true);
  });

  it('goes back to the detail page on Cancel without saving', async () => {
    await open();
    button('Cancel')!.click();
    await settle();

    expect(update).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).toBe('/sightings/sighting-1');
  });

  it('shows the cannot-edit alert with a Back link and no form to someone who is not the owner', async () => {
    get.mockReturnValue(of(notOwned));
    const page = await open();
    const back = page.querySelector<HTMLAnchorElement>('mat-card-actions a');

    expect(page.querySelector('app-alert')?.textContent?.trim()).toBe(
      'You can only edit sightings you reported.',
    );
    expect(page.querySelector('form')).toBeNull();
    expect(back?.textContent?.trim()).toBe('Back to sighting');
    expect(back?.getAttribute('href')).toBe('/sightings/sighting-1');
    expect(report).not.toHaveBeenCalled();
  });

  it('shows the removed alert with a Back link and no form to the reporter of a removed sighting', async () => {
    get.mockReturnValue(of({ ...owned, removedAt: '2026-11-17T10:00:00Z', editPermissions: null }));
    const page = await open();
    const back = page.querySelector<HTMLAnchorElement>('mat-card-actions a');

    expect(page.querySelector('app-alert')?.textContent?.trim()).toBe(
      "This sighting was removed by an admin and can't be edited.",
    );
    expect(page.querySelector('form')).toBeNull();
    expect(back?.textContent?.trim()).toBe('Back to sighting');
    expect(back?.getAttribute('href')).toBe('/sightings/sighting-1');
    expect(report).not.toHaveBeenCalled();
  });

  it('shows the not-found alert with a Back link for a 404 without reporting', async () => {
    get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const page = await open('missing');

    expect(page.querySelector('app-alert')?.textContent?.trim()).toBe(
      "This sighting doesn't exist or is no longer available.",
    );
    expect(page.querySelector('form')).toBeNull();
    expect(page.querySelector('mat-card-actions a')?.getAttribute('href')).toBe(
      '/sightings/missing',
    );
    expect(report).not.toHaveBeenCalled();
  });

  it('reports a failed load, shows its reference and loads again on Try again', async () => {
    const failure = new HttpErrorResponse({ status: 500, error: { traceId } });

    get.mockReturnValue(throwError(() => failure));
    const page = await open();

    expect(page.querySelector('app-alert[role="alert"]')?.textContent).toContain(
      "We couldn't reach the sightings right now. Try again.",
    );
    expect(page.querySelector('app-error-reference')?.textContent).toContain(traceId);
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.edit.load', failure);

    get.mockReturnValue(of(owned));
    button('Try again')!.click();
    await settle();

    expect(get).toHaveBeenCalledTimes(2);
    expect(page.querySelector('form')).not.toBeNull();
  });

  it('keeps the error state with Try again when the first load gets no answer', async () => {
    const failure = new HttpErrorResponse({ status: 0 });

    get.mockReturnValue(throwError(() => failure));
    const page = await open();

    expect(page.querySelector('form')).toBeNull();
    expect(button('Try again')).not.toBeNull();
    expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.edit.load', failure);
  });

  it('shows a loading indicator and no form while the sighting loads', async () => {
    get.mockReturnValue(NEVER);
    const page = await open();

    expect(page.querySelector('app-loading-indicator')).not.toBeNull();
    expect(page.querySelector('form')).toBeNull();
  });

  describe('photo', () => {
    it('shows no current photo and sends no thumbnail request when the sighting has none', async () => {
      await open();

      expect(currentThumbnail()).toBeNull();
      expect(button('Remove current photo')).toBeNull();
      expect(photoThumbnail).not.toHaveBeenCalled();
      expect(root().querySelector('app-photo-picker')).not.toBeNull();
    });

    it('keeps the current photo by default', async () => {
      get.mockReturnValue(of(withPhoto));
      await open();

      expect(photoThumbnail).toHaveBeenCalledExactlyOnceWith('sighting-1');
      expect(currentThumbnail()?.getAttribute('src')).toBe('blob:preview-1');
      expect(currentThumbnail()?.getAttribute('alt')).toBe('Photo of Limpy');

      await submit();

      expect(updateSent()).toMatchObject({ photo: null, removePhoto: false });
    });

    it('removes the current photo with Remove current photo', async () => {
      get.mockReturnValue(of(withPhoto));
      await open();
      button('Remove current photo')!.click();
      await settle();

      expect(currentThumbnail()).toBeNull();
      expect(button('Remove current photo')).toBeNull();

      await submit();

      expect(updateSent()).toMatchObject({ photo: null, removePhoto: true });
    });

    it('replaces the current photo with a newly chosen one', async () => {
      get.mockReturnValue(of(withPhoto));
      await open();
      await chooseNewPhoto();

      expect(currentThumbnail()).toBeNull();

      await submit();

      expect(updateSent()).toMatchObject({ photo: encodedPhoto, removePhoto: false });
    });

    it('replaces rather than removes when a new photo is chosen after Remove', async () => {
      get.mockReturnValue(of(withPhoto));
      await open();
      button('Remove current photo')!.click();
      await settle();
      await chooseNewPhoto();
      await submit();

      expect(updateSent()).toMatchObject({ photo: encodedPhoto, removePhoto: false });
    });

    it('adds a photo to a sighting that had none', async () => {
      await open();
      await chooseNewPhoto();
      await submit();

      expect(updateSent()).toMatchObject({ photo: encodedPhoto, removePhoto: false });
    });

    it('shows an inline message and reports when the current photo fails to load', async () => {
      const failure = new HttpErrorResponse({ status: 500 });

      get.mockReturnValue(of(withPhoto));
      photoThumbnail.mockReturnValue(throwError(() => failure));
      await open();

      expect(currentThumbnail()).toBeNull();
      expect(root().querySelector('.edit-sighting__current-photo app-alert')?.textContent?.trim()).toBe(
        "We couldn't load the photo right now.",
      );
      expect(button('Remove current photo')).not.toBeNull();
      expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.edit.photo', failure);
    });

    it('moves focus to the photo picker after Remove current photo', async () => {
      get.mockReturnValue(of(withPhoto));
      await open();
      button('Remove current photo')!.click();
      await settle();

      expect(document.activeElement).toBe(button('Choose photo'));
    });

    it('does not load the thumbnail again when a stale reload keeps the same photo', async () => {
      update.mockReturnValue(throwError(() => badRequest('DescriptionLocked')));
      get.mockReturnValue(of(withPhoto));
      await open();
      get.mockReturnValue(of({ ...withPhoto, editPermissions: locked.editPermissions }));
      await submit();

      expect(get).toHaveBeenCalledTimes(2);
      expect(photoThumbnail).toHaveBeenCalledOnce();
      expect(revokePreviewUrl).not.toHaveBeenCalled();
      expect(currentThumbnail()?.getAttribute('src')).toBe('blob:preview-1');
    });

    it('revokes the thumbnail URL when the page is destroyed', async () => {
      get.mockReturnValue(of(withPhoto));
      await open();
      harness.fixture.destroy();

      expect(revokePreviewUrl).toHaveBeenCalledWith('blob:preview-1');
    });

    it('shows a rejected new photo as a photo error without reporting', async () => {
      update.mockReturnValue(throwError(() => badRequest('PhotoUnreadable')));
      await open();
      await chooseNewPhoto();
      await submit();

      expect(root().querySelector('.photo-picker__error')?.textContent?.trim()).toBe(
        "We couldn't read this photo. Choose another one or remove it.",
      );
      expect(formAlert()).toBeNull();
      expect(report).not.toHaveBeenCalled();
    });
  });

  describe('save failures', () => {
    it('reloads a stale form after DescriptionLocked, keeps the other edits and locks the description', async () => {
      update.mockReturnValue(throwError(() => badRequest('DescriptionLocked')));
      await open();
      setValue(nameInput(), 'Limpy Jr');
      setValue(textareas()[0], 'A changed description');
      await settle();
      get.mockReturnValue(of(locked));
      await submit();

      expect(get).toHaveBeenCalledTimes(2);
      expect(formAlert()?.textContent?.trim()).toContain(
        'Someone else has just commented on this sighting or contacted you',
      );
      expect(textareas()[0].disabled).toBe(true);
      expect(textareas()[0].value).toBe('Grey pigeon with a ruffled wing');
      expect(nameInput().value).toBe('Limpy Jr');
      expect(report).not.toHaveBeenCalled();
    });

    it('reloads a stale form after InvalidStatusTransition and resets a status that is no longer offered', async () => {
      update.mockReturnValue(throwError(() => badRequest('InvalidStatusTransition')));
      await open();
      const [status] = await selects();

      await choose(status, 'Not found');
      get.mockReturnValue(
        of({
          ...owned,
          status: SightingStatus.caught,
          editPermissions: {
            canEditDescription: true,
            allowedNextStatuses: [SightingStatus.atTheVet, SightingStatus.inHomeCare],
          },
        }),
      );
      await submit();

      expect(get).toHaveBeenCalledTimes(2);
      expect(formAlert()?.textContent?.trim()).toContain('this status change is no longer possible');
      expect(await status.getValueText()).toBe('Caught');
      expect(await optionLabels(status)).toEqual(['Caught', 'At the vet', 'In home care']);
      expect(report).not.toHaveBeenCalled();
    });

    it('keeps the form and its alert while the stale-form reload is in flight', async () => {
      const reload = new Subject<Sighting>();

      update.mockReturnValue(throwError(() => badRequest('DescriptionLocked')));
      await open();
      get.mockReturnValue(reload);
      await submit();

      expect(root().querySelector('form')).not.toBeNull();
      expect(root().querySelector('app-loading-indicator')).toBeNull();
      expect(formAlert()).not.toBeNull();
    });

    it('keeps the form and the edits when the stale-form reload fails, and reports it', async () => {
      const failure = new HttpErrorResponse({ status: 500, error: { traceId } });

      update.mockReturnValue(throwError(() => badRequest('DescriptionLocked')));
      await open();
      setValue(nameInput(), 'Limpy Jr');
      setValue(textareas()[1], 'Walks slowly');
      await settle();
      get.mockReturnValue(throwError(() => failure));
      await submit();

      expect(get).toHaveBeenCalledTimes(2);
      expect(root().querySelector('form')).not.toBeNull();
      expect(root().querySelector('app-error-retry')).toBeNull();
      expect(nameInput().value).toBe('Limpy Jr');
      expect(textareas()[1].value).toBe('Walks slowly');
      expect(formAlert()?.textContent).toContain(
        "We couldn't save your changes right now. Try again.",
      );
      expect(formAlert()?.querySelector('app-error-reference')?.textContent).toContain(traceId);
      expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.edit.load', failure);

      update.mockReturnValue(of(owned));
      await submit();

      expect(update).toHaveBeenCalledTimes(2);
      expect(update.mock.calls[1][1]).toMatchObject({
        name: 'Limpy Jr',
        behaviour: 'Walks slowly',
      });
    });

    it('keeps the form and shows the not-found message when the stale-form reload gets a 404', async () => {
      update.mockReturnValue(throwError(() => badRequest('InvalidStatusTransition')));
      await open();
      setValue(nameInput(), 'Limpy Jr');
      await settle();
      get.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
      await submit();

      expect(root().querySelector('form')).not.toBeNull();
      expect(nameInput().value).toBe('Limpy Jr');
      expect(formAlert()?.textContent?.trim()).toBe(
        "This sighting doesn't exist or is no longer available.",
      );
      expect(report).not.toHaveBeenCalled();
    });

    it('shows the too-many-edits message for a 429 without reporting and keeps the values', async () => {
      update.mockReturnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 429,
              error: { status: 429, title: 'Too Many Requests', traceId },
            }),
        ),
      );
      await open();
      setValue(nameInput(), 'Limpy Jr');
      await submit();

      expect(formAlert()?.textContent?.trim()).toBe(
        "You've saved a lot of changes in a short time. Wait a minute and try again.",
      );
      expect(formAlert()?.querySelector('app-error-reference')?.textContent?.trim() ?? '').toBe('');
      expect(nameInput().value).toBe('Limpy Jr');
      expect(get).toHaveBeenCalledOnce();
      expect(report).not.toHaveBeenCalled();
    });

    it('reports a 403 as a client bug and shows the forbidden message with its reference', async () => {
      const failure = new HttpErrorResponse({ status: 403, error: { traceId } });

      update.mockReturnValue(throwError(() => failure));
      await open();
      await submit();

      expect(formAlert()?.textContent).toContain(
        'Only the person who reported this sighting can change it.',
      );
      expect(formAlert()?.querySelector('app-error-reference')?.textContent).toContain(traceId);
      expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.edit', failure);
      expect(get).toHaveBeenCalledOnce();
    });

    it('reports PhotoConflict as a client bug with its reference', async () => {
      const failure = badRequest('PhotoConflict');

      update.mockReturnValue(throwError(() => failure));
      await open();
      await submit();

      expect(formAlert()?.textContent).toContain(
        "We couldn't tell whether to replace or remove the photo.",
      );
      expect(formAlert()?.querySelector('app-error-reference')?.textContent).toContain(traceId);
      expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.edit', failure);
    });

    it('shows the not-found message on save without reporting', async () => {
      update.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
      await open();
      await submit();

      expect(formAlert()?.textContent?.trim()).toBe(
        "This sighting doesn't exist or is no longer available.",
      );
      expect(report).not.toHaveBeenCalled();
    });

    it('reports the photo storage outage and keeps the form values', async () => {
      const failure = new HttpErrorResponse({
        status: 503,
        error: { status: 503, traceId, errors: { PhotoStorageUnavailable: ['x'] } },
      });

      update.mockReturnValue(throwError(() => failure));
      await open();
      setValue(nameInput(), 'Limpy Jr');
      await chooseNewPhoto();
      await submit();

      expect(formAlert()?.textContent).toContain(
        "We couldn't save the photo right now. Try again, or keep the current photo.",
      );
      expect(nameInput().value).toBe('Limpy Jr');
      expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.edit', failure);
    });

    it('reports an unexpected failure and shows the generic edit message with its reference', async () => {
      const failure = new HttpErrorResponse({ status: 500, error: { traceId } });

      update.mockReturnValue(throwError(() => failure));
      await open();
      await submit();

      expect(formAlert()?.textContent).toContain(
        "We couldn't save your changes right now. Try again.",
      );
      expect(formAlert()?.querySelector('app-error-reference')?.textContent).toContain(traceId);
      expect(report).toHaveBeenCalledExactlyOnceWith('api-failure', 'sighting.edit', failure);
      expect(TestBed.inject(Router).url).toBe('/sightings/sighting-1/edit');
    });

    it('keeps a validation 400 silent', async () => {
      update.mockReturnValue(throwError(() => badRequest('InvalidStatus')));
      await open();
      await submit();

      expect(formAlert()?.textContent).toContain("We couldn't save your changes right now.");
      expect(formAlert()?.querySelector('app-error-reference')?.textContent?.trim() ?? '').toBe('');
      expect(report).not.toHaveBeenCalled();
    });

    it('clears the alert when a new save starts', async () => {
      update.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
      update.mockReturnValueOnce(NEVER);
      await open();
      await submit();

      expect(formAlert()).not.toBeNull();

      await submit();

      expect(formAlert()).toBeNull();
    });
  });
});
