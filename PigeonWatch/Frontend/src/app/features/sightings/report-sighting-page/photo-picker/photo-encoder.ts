import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, defer, switchMap, throwError } from 'rxjs';

export const photoJpegType = 'image/jpeg';
export const photoJpegQuality = 0.85;

export interface PhotoSize {
  readonly width: number;
  readonly height: number;
}

export class PhotoDecodeError extends Error {
  override readonly name = 'PhotoDecodeError';

  constructor(cause: unknown) {
    super('The browser could not decode the chosen photo.', { cause });
  }
}

export class PhotoEncodeError extends Error {
  override readonly name = 'PhotoEncodeError';

  constructor(message: string) {
    super(message);
  }
}

export function scaledPhotoSize(width: number, height: number, maxDimension: number): PhotoSize {
  const scale = Math.min(1, maxDimension / Math.max(width, height));

  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
  };
}

@Injectable({ providedIn: 'root' })
export class PhotoEncoder {
  private readonly document = inject(DOCUMENT);

  encode(file: Blob, maxDimension: number): Observable<Blob> {
    return defer(() => createImageBitmap(file, { imageOrientation: 'from-image' })).pipe(
      catchError((cause: unknown) => throwError(() => new PhotoDecodeError(cause))),
      switchMap((bitmap) => this.toJpeg(bitmap, maxDimension)),
    );
  }

  createPreviewUrl(photo: Blob): string {
    return URL.createObjectURL(photo);
  }

  revokePreviewUrl(url: string): void {
    URL.revokeObjectURL(url);
  }

  private toJpeg(bitmap: ImageBitmap, maxDimension: number): Observable<Blob> {
    return new Observable<Blob>((subscriber) => {
      const size = scaledPhotoSize(bitmap.width, bitmap.height, maxDimension);
      const canvas = this.document.createElement('canvas');

      canvas.width = size.width;
      canvas.height = size.height;

      const context = canvas.getContext('2d');

      if (!context) {
        bitmap.close();
        subscriber.error(new PhotoEncodeError('The canvas 2D context is unavailable.'));

        return;
      }

      context.drawImage(bitmap, 0, 0, size.width, size.height);
      bitmap.close();
      canvas.toBlob(
        (blob) => {
          if (!blob) {
            subscriber.error(new PhotoEncodeError('The canvas produced no JPEG output.'));

            return;
          }

          subscriber.next(blob);
          subscriber.complete();
        },
        photoJpegType,
        photoJpegQuality,
      );
    });
  }
}
