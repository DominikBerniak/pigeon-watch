import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ErrorRetry } from './error-retry';

@Component({
  imports: [ErrorRetry],
  template: `<app-error-retry retryLabel="Try again" (retry)="retries = retries + 1"
    >Something went wrong</app-error-retry
  >`,
})
class ErrorRetryHost {
  retries = 0;
}

describe('ErrorRetry', () => {
  async function render(): Promise<{ host: ErrorRetryHost; element: HTMLElement }> {
    const fixture = TestBed.createComponent(ErrorRetryHost);
    await fixture.whenStable();

    return {
      host: fixture.componentInstance,
      element: fixture.nativeElement.querySelector('app-error-retry'),
    };
  }

  it('shows the projected message in an error alert and the retry label on a button', async () => {
    const { element } = await render();
    const button = element.querySelector('button');

    expect(element.querySelector('app-alert[role="alert"]')?.textContent?.trim()).toBe(
      'Something went wrong',
    );
    expect(button?.type).toBe('button');
    expect(button?.textContent?.trim()).toBe('Try again');
  });

  it('emits retry when the button is pressed', async () => {
    const { host, element } = await render();

    element.querySelector('button')?.click();

    expect(host.retries).toBe(1);
  });

  it('keeps the button at its natural width, aligned to the start', async () => {
    const { element } = await render();
    const button = element.querySelector<HTMLElement>('button')!;

    expect(getComputedStyle(element).flexDirection).toBe('column');
    expect(getComputedStyle(button).alignSelf).toBe('flex-start');
  });
});
