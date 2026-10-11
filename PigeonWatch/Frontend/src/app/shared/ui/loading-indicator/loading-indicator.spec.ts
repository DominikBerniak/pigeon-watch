import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { LoadingIndicator, LoadingIndicatorLayout } from './loading-indicator';

@Component({
  imports: [LoadingIndicator],
  template: `<app-loading-indicator label="Loading…" [layout]="layout()" [live]="live()" />`,
})
class LoadingIndicatorHost {
  readonly layout = signal<LoadingIndicatorLayout>('block');
  readonly live = signal(true);
}

describe('LoadingIndicator', () => {
  async function render(
    layout: LoadingIndicatorLayout = 'block',
    live = true,
  ): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(LoadingIndicatorHost);
    fixture.componentInstance.layout.set(layout);
    fixture.componentInstance.live.set(live);
    await fixture.whenStable();

    return fixture.nativeElement.querySelector('app-loading-indicator');
  }

  it('is a centred live status with a labelled spinner and no visible text by default', async () => {
    const host = await render();
    const spinner = host.querySelector('mat-progress-spinner');

    expect(host.getAttribute('role')).toBe('status');
    expect(getComputedStyle(host).display).toBe('flex');
    expect(getComputedStyle(host).justifyContent).toBe('center');
    expect(spinner?.getAttribute('aria-label')).toBe('Loading…');
    expect(spinner?.getAttribute('mode')).toBe('indeterminate');
    expect(host.textContent?.trim()).toBe('');
  });

  it('shows a smaller decorative spinner beside the exposed label text when inline', async () => {
    const block = await render('block');
    const blockSize = block.querySelector<HTMLElement>('mat-progress-spinner')?.style.width;
    const inline = await render('inline');
    const spinner = inline.querySelector<HTMLElement>('mat-progress-spinner');
    const text = inline.querySelector('span');

    expect(inline.classList).toContain('loading-indicator--inline');
    expect(getComputedStyle(inline).display).toBe('inline-flex');
    expect(spinner?.getAttribute('aria-hidden')).toBe('true');
    expect(spinner?.hasAttribute('aria-label')).toBe(false);
    expect(text?.textContent?.trim()).toBe('Loading…');
    expect(text?.closest('[aria-hidden="true"]')).toBeNull();
    expect(inline.textContent?.trim()).toBe('Loading…');
    expect(Number.parseFloat(spinner?.style.width ?? '')).toBeLessThan(
      Number.parseFloat(blockSize ?? ''),
    );
  });

  it('carries no live region when live is off', async () => {
    const host = await render('inline', false);

    expect(host.hasAttribute('role')).toBe(false);
  });
});
