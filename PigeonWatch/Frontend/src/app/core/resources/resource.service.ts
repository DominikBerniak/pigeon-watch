import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, defer, from, map, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SKIP_AUTH, SKIP_WARMUP } from '../http/http-context-tokens';
import { CultureStore } from './culture-store';
import { UiLabels, defaultCulture, defaultLabels, snapshots } from './generated/snapshots';

export interface UiResourcesResponse {
  culture: string;
  labels: Record<string, string>;
}

@Injectable({ providedIn: 'root' })
export class ResourceService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly cultureStore = inject(CultureStore);
  private readonly cultureState = signal<string>(defaultCulture);
  private readonly labelsState = signal<UiLabels>(defaultLabels);
  private readonly warnedKeys = new Set<string>();
  private readonly requestedCulture = this.cultureStore.selected();
  private servedByApi = false;

  readonly culture = this.cultureState.asReadonly();
  readonly labels = this.labelsState.asReadonly();

  constructor() {
    this.apply(defaultCulture, defaultLabels);

    if (this.requestedCulture === defaultCulture) return;

    this.loadSnapshot(this.requestedCulture).subscribe();
  }

  t(key: string, ...args: unknown[]): string {
    const labels = this.labelsState();

    if (!Object.hasOwn(labels, key)) {
      this.warnMissing(key);

      return key;
    }

    const template = labels[key];

    if (args.length === 0) return template;

    return template.replace(/\{(\d+)\}/g, (placeholder, index: string) => {
      const position = Number(index);

      return position < args.length ? String(args[position]) : placeholder;
    });
  }

  load(): Observable<void> {
    const context = new HttpContext().set(SKIP_AUTH, true).set(SKIP_WARMUP, true);
    const url = `${environment.apiUrl}/resources/${encodeURIComponent(this.requestedCulture)}`;

    return this.http.get<UiResourcesResponse>(url, { context }).pipe(
      tap((response) => {
        if (!response || typeof response.labels !== 'object' || response.labels === null) return;

        this.servedByApi = true;
        this.apply(response.culture || this.requestedCulture, response.labels);
      }),
      map(() => undefined),
      catchError(() => of(undefined)),
    );
  }

  private loadSnapshot(culture: string): Observable<void> {
    const loader = snapshots[culture];

    if (!loader) return of(undefined);

    return defer(() => from(loader())).pipe(
      tap((labels) => {
        if (!this.servedByApi) this.apply(culture, labels);
      }),
      map(() => undefined),
      catchError(() => of(undefined)),
    );
  }

  private apply(culture: string, labels: UiLabels): void {
    this.cultureState.set(culture);
    this.labelsState.set(labels);
    this.document.documentElement.lang = culture;
  }

  private warnMissing(key: string): void {
    if (this.warnedKeys.has(key)) return;

    this.warnedKeys.add(key);
    console.warn(`Missing UI label for key "${key}".`);
  }
}
