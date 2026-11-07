import { TestBed } from '@angular/core/testing';
import { Observable, lastValueFrom } from 'rxjs';
import {
  PhotoDecodeError,
  PhotoEncodeError,
  PhotoEncoder,
  photoJpegQuality,
  scaledPhotoSize,
} from './photo-encoder';

interface FakeContext {
  drawImage: ReturnType<typeof vi.fn>;
}

describe('PhotoEncoder', () => {
  let encoder: PhotoEncoder;
  let close: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    close = vi.fn();
    encoder = TestBed.inject(PhotoEncoder);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  function stubBitmap(width: number, height: number): ReturnType<typeof vi.fn> {
    const create = vi.fn(() => Promise.resolve({ width, height, close }));

    vi.stubGlobal('createImageBitmap', create);

    return create;
  }

  function stubCanvas(output: Blob | null, context: FakeContext | null): HTMLCanvasElement[] {
    const canvases: HTMLCanvasElement[] = [];
    const createElement = document.createElement.bind(document);

    vi.spyOn(document, 'createElement').mockImplementation((tag: string) => {
      const element = createElement(tag);

      if (element instanceof HTMLCanvasElement) {
        vi.spyOn(element, 'getContext').mockReturnValue(
          context as unknown as CanvasRenderingContext2D,
        );
        vi.spyOn(element, 'toBlob').mockImplementation((callback) => callback(output));
        canvases.push(element);
      }

      return element;
    });

    return canvases;
  }

  function failureOf(result: Observable<Blob>): Promise<unknown> {
    return lastValueFrom(result).then(
      () => undefined,
      (failure: unknown) => failure,
    );
  }

  it('scales the longest edge down to the maximum and never up', () => {
    expect(scaledPhotoSize(4000, 3000, 1600)).toEqual({ width: 1600, height: 1200 });
    expect(scaledPhotoSize(3000, 4000, 1600)).toEqual({ width: 1200, height: 1600 });
    expect(scaledPhotoSize(800, 600, 1600)).toEqual({ width: 800, height: 600 });
    expect(scaledPhotoSize(10000, 1, 1600)).toEqual({ width: 1600, height: 1 });
  });

  it('decodes with the EXIF orientation applied and draws a scaled JPEG', async () => {
    const create = stubBitmap(4000, 3000);
    const context: FakeContext = { drawImage: vi.fn() };
    const jpeg = new Blob(['jpeg'], { type: 'image/jpeg' });
    const canvases = stubCanvas(jpeg, context);
    const file = new Blob(['original'], { type: 'image/png' });

    const result = await lastValueFrom(encoder.encode(file, 1600));

    expect(result).toBe(jpeg);
    expect(create).toHaveBeenCalledExactlyOnceWith(file, { imageOrientation: 'from-image' });
    expect(canvases[0].width).toBe(1600);
    expect(canvases[0].height).toBe(1200);
    expect(context.drawImage).toHaveBeenCalledWith(expect.anything(), 0, 0, 1600, 1200);
    expect(canvases[0].toBlob).toHaveBeenCalledWith(
      expect.any(Function),
      'image/jpeg',
      photoJpegQuality,
    );
    expect(close).toHaveBeenCalledOnce();
  });

  it('turns a decode failure into a PhotoDecodeError that keeps the cause', async () => {
    const cause = new DOMException('The source image could not be decoded.', 'InvalidStateError');
    vi.stubGlobal(
      'createImageBitmap',
      vi.fn(() => Promise.reject(cause)),
    );

    const error = await failureOf(encoder.encode(new Blob(['heic']), 1600));

    expect(error).toBeInstanceOf(PhotoDecodeError);
    expect((error as PhotoDecodeError).cause).toBe(cause);
  });

  it('closes a bitmap that finishes decoding after the encoding was cancelled', async () => {
    let resolveBitmap: (bitmap: unknown) => void = () => undefined;
    const decoding = new Promise<unknown>((resolve) => (resolveBitmap = resolve));
    vi.stubGlobal(
      'createImageBitmap',
      vi.fn(() => decoding),
    );
    const context: FakeContext = { drawImage: vi.fn() };
    stubCanvas(new Blob(['jpeg']), context);

    const subscription = encoder.encode(new Blob(['x']), 1600).subscribe();

    subscription.unsubscribe();
    resolveBitmap({ width: 100, height: 100, close });
    await decoding;
    await Promise.resolve();

    expect(close).toHaveBeenCalledOnce();
    expect(context.drawImage).not.toHaveBeenCalled();
  });

  it('fails with a PhotoEncodeError when the canvas has no 2D context', async () => {
    stubBitmap(100, 100);
    stubCanvas(null, null);

    const error = await failureOf(encoder.encode(new Blob(['x']), 1600));

    expect(error).toBeInstanceOf(PhotoEncodeError);
    expect(close).toHaveBeenCalledOnce();
  });

  it('fails with a PhotoEncodeError when the canvas produces no output', async () => {
    stubBitmap(100, 100);
    stubCanvas(null, { drawImage: vi.fn() });

    const error = await failureOf(encoder.encode(new Blob(['x']), 1600));

    expect(error).toBeInstanceOf(PhotoEncodeError);
  });
});
