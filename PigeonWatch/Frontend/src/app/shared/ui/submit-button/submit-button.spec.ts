import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SubmitButton, SubmitButtonTone } from './submit-button';

@Component({
  imports: [SubmitButton],
  template: `<app-submit-button [tone]="tone()" [busy]="busy()" [disabled]="disabled()"
    >Save</app-submit-button
  >`,
})
class SubmitButtonHost {
  readonly tone = signal<SubmitButtonTone>('primary');
  readonly busy = signal(false);
  readonly disabled = signal(false);
}

describe('SubmitButton', () => {
  async function render(busy: boolean): Promise<HTMLButtonElement> {
    const fixture = TestBed.createComponent(SubmitButtonHost);
    fixture.componentInstance.busy.set(busy);
    await fixture.whenStable();

    return fixture.nativeElement.querySelector('button');
  }

  it('is an enabled submit button with the projected label when idle', async () => {
    const button = await render(false);

    expect(button.type).toBe('submit');
    expect(button.disabled).toBe(false);
    expect(button.hasAttribute('aria-busy')).toBe(false);
    expect(button.querySelector('mat-progress-spinner')).toBeNull();
    expect(button.textContent?.trim()).toBe('Save');
  });

  it('defaults to the primary tone', async () => {
    const fixture = TestBed.createComponent(SubmitButtonHost);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement.querySelector('app-submit-button');

    expect(host.getAttribute('data-tone')).toBe('primary');
  });

  it('reflects the success tone on the host', async () => {
    const fixture = TestBed.createComponent(SubmitButtonHost);
    fixture.componentInstance.tone.set('success');
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement.querySelector('app-submit-button');

    expect(host.getAttribute('data-tone')).toBe('success');
  });

  it('is disabled without a spinner or aria-busy when disabled', async () => {
    const fixture = TestBed.createComponent(SubmitButtonHost);
    fixture.componentInstance.disabled.set(true);
    await fixture.whenStable();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button');

    expect(button.disabled).toBe(true);
    expect(button.hasAttribute('aria-busy')).toBe(false);
    expect(button.querySelector('mat-progress-spinner')).toBeNull();
  });

  it('is disabled, aria-busy and shows the spinner when busy', async () => {
    const button = await render(true);

    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.querySelector('mat-progress-spinner')).not.toBeNull();
  });
});
