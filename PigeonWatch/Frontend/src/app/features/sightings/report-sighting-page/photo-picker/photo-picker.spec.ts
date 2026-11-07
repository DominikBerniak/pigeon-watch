import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormField, form } from '@angular/forms/signals';
import { Observable, Subject, of, throwError } from 'rxjs';
import { ErrorReportingService } from '../../../../core/error-reporting/error-reporting.service';
import { FormSubmitState } from '../../../../shared/ui/form-field/form-submit-state';
import { PhotoDecodeError, PhotoEncodeError, PhotoEncoder } from '../../shared/photo-encoder';
import { PhotoPicker } from './photo-picker';

interface ReportModel {
  photo: Blob | null;
}

@Component({
  imports: [FormField, PhotoPicker],
  template: `<app-photo-picker
    [formField]="report.photo"
    (processingChange)="processing.set($event)"
  />`,
})
class PickerHost {
  readonly model = signal<ReportModel>({ photo: null });
  readonly report = form(this.model);
  readonly processing = signal(false);
}

const encoded = new Blob(['encoded-jpeg'], { type: 'image/jpeg' });
const unsupportedText = "This file type isn't supported. Choose a JPEG, PNG or WebP photo.";

describe('PhotoPicker', () => {
  let encode: ReturnType<typeof vi.fn<(file: Blob, maxDimension: number) => Observable<Blob>>>;
  let createPreviewUrl: ReturnType<typeof vi.fn<(photo: Blob) => string>>;
  let revokePreviewUrl: ReturnType<typeof vi.fn<(url: string) => void>>;
  let report: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<PickerHost>;
  let previews: number;

  beforeEach(async () => {
    previews = 0;
    encode = vi.fn<(file: Blob, maxDimension: number) => Observable<Blob>>(() => of(encoded));
    createPreviewUrl = vi.fn<(photo: Blob) => string>(() => `blob:preview-${++previews}`);
    revokePreviewUrl = vi.fn<(url: string) => void>();
    report = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        FormSubmitState,
        { provide: PhotoEncoder, useValue: { encode, createPreviewUrl, revokePreviewUrl } },
        { provide: ErrorReportingService, useValue: { report } },
      ],
    });
    fixture = TestBed.createComponent(PickerHost);
    await fixture.whenStable();
  });

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function fileInput(): HTMLInputElement {
    const input = root().querySelector<HTMLInputElement>('input[type="file"]');

    if (!input) throw new Error('No file input');

    return input;
  }

  function button(text: string): HTMLButtonElement | undefined {
    return [...root().querySelectorAll<HTMLButtonElement>('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    );
  }

  async function pick(file: File | null): Promise<void> {
    Object.defineProperty(fileInput(), 'files', {
      configurable: true,
      value: { length: file ? 1 : 0, item: (index: number) => (index === 0 ? file : null) },
    });
    fileInput().dispatchEvent(new Event('change'));
    await settle();
  }

  function photo(): Blob | null {
    return fixture.componentInstance.model().photo;
  }

  function preview(): HTMLImageElement | null {
    return root().querySelector<HTMLImageElement>('img');
  }

  function errorText(): string | undefined {
    return root().querySelector('.photo-picker__error')?.textContent?.trim();
  }

  function jpegFile(): File {
    return new File(['original'], 'IMG_0001.jpg', { type: 'image/jpeg' });
  }

  it('offers a visible choose button and a file input limited to the allowed types', () => {
    expect(root().querySelector('legend')?.textContent?.trim()).toBe('Photo (optional)');
    expect(button('Choose photo')).toBeDefined();
    expect(button('Remove photo')).toBeUndefined();
    expect(fileInput().accept).toBe('image/jpeg,image/png,image/webp');
    expect(fileInput().hidden).toBe(true);
    expect(preview()).toBeNull();
  });

  it('opens the file chooser from the choose button', () => {
    const click = vi.spyOn(fileInput(), 'click').mockImplementation(() => undefined);

    button('Choose photo')!.click();

    expect(click).toHaveBeenCalledOnce();
  });

  it('re-encodes the chosen photo, stores the result and previews it', async () => {
    const file = jpegFile();

    await pick(file);

    expect(encode).toHaveBeenCalledExactlyOnceWith(file, 1600);
    expect(photo()).toBe(encoded);
    expect(createPreviewUrl).toHaveBeenCalledExactlyOnceWith(encoded);
    expect(preview()?.getAttribute('src')).toBe('blob:preview-1');
    expect(preview()?.alt).toBe('Preview of the chosen photo');
    expect(button('Remove photo')).toBeDefined();
    expect(errorText()).toBe('');
  });

  it('revokes the old preview when another photo is chosen', async () => {
    await pick(jpegFile());
    encode.mockReturnValue(of(new Blob(['second'], { type: 'image/jpeg' })));

    await pick(jpegFile());

    expect(revokePreviewUrl).toHaveBeenCalledExactlyOnceWith('blob:preview-1');
    expect(preview()?.getAttribute('src')).toBe('blob:preview-2');
  });

  it('removes the photo, revokes its preview and returns focus to the choose button', async () => {
    await pick(jpegFile());

    button('Remove photo')!.click();
    await settle();

    expect(photo()).toBeNull();
    expect(preview()).toBeNull();
    expect(revokePreviewUrl).toHaveBeenCalledExactlyOnceWith('blob:preview-1');
    expect(button('Remove photo')).toBeUndefined();
    expect(document.activeElement).toBe(button('Choose photo'));
  });

  it('revokes the preview when it is destroyed', async () => {
    await pick(jpegFile());

    fixture.destroy();

    expect(revokePreviewUrl).toHaveBeenCalledExactlyOnceWith('blob:preview-1');
  });

  it('shows the unsupported type error when the browser cannot decode the photo', async () => {
    encode.mockReturnValue(throwError(() => new PhotoDecodeError(new Error('decode'))));

    await pick(new File(['heic'], 'IMG_0002.jpg', { type: 'image/jpeg' }));

    expect(photo()).toBeNull();
    expect(preview()).toBeNull();
    expect(errorText()).toBe(unsupportedText);
    expect(report).not.toHaveBeenCalled();
  });

  it('rejects a type outside the allowed list without decoding it', async () => {
    await pick(new File(['heic'], 'IMG_0003.heic', { type: 'image/heic' }));

    expect(encode).not.toHaveBeenCalled();
    expect(photo()).toBeNull();
    expect(errorText()).toBe(unsupportedText);
  });

  it('rejects a re-encoded photo above the size limit with the too large error', async () => {
    encode.mockReturnValue(of(new Blob([new Uint8Array(2097153)], { type: 'image/jpeg' })));

    await pick(jpegFile());

    expect(photo()).toBeNull();
    expect(errorText()).toBe('This photo is too large. Choose a smaller one or remove it.');
  });

  it('drops an earlier photo when the next pick is rejected and clears the error on a good pick', async () => {
    await pick(jpegFile());
    encode.mockReturnValueOnce(throwError(() => new PhotoDecodeError(new Error('decode'))));

    await pick(jpegFile());

    expect(photo()).toBeNull();
    expect(errorText()).toBe(unsupportedText);

    await pick(jpegFile());

    expect(photo()).toBe(encoded);
    expect(errorText()).toBe('');
  });

  it('reports an unexpected encoding failure and shows the unreadable error', async () => {
    const failure = new PhotoEncodeError('The canvas produced no JPEG output.');
    encode.mockReturnValue(throwError(() => failure));

    await pick(jpegFile());

    expect(photo()).toBeNull();
    expect(errorText()).toBe("We couldn't read this photo. Choose another one or remove it.");
    expect(report).toHaveBeenCalledExactlyOnceWith('fallback', 'sighting.photo.encode', failure);
  });

  it('signals processing while the photo is being encoded', async () => {
    const pending = new Subject<Blob>();
    encode.mockReturnValue(pending);

    await pick(jpegFile());

    expect(fixture.componentInstance.processing()).toBe(true);
    expect(button('Choose photo')!.disabled).toBe(true);
    expect(root().querySelector('[role="status"]')?.textContent?.trim()).toBe(
      'Preparing the photo…',
    );

    pending.next(encoded);
    pending.complete();
    await settle();

    expect(fixture.componentInstance.processing()).toBe(false);
    expect(root().querySelector('[role="status"]')).toBeNull();
    expect(photo()).toBe(encoded);
  });

  it('ignores a change event without a file', async () => {
    await pick(null);

    expect(encode).not.toHaveBeenCalled();
    expect(photo()).toBeNull();
  });
});
