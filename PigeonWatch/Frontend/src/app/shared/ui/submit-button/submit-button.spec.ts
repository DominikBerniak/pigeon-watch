import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SubmitButton } from './submit-button';

@Component({
  imports: [SubmitButton],
  template: `<app-submit-button [busy]="busy()">Save</app-submit-button>`,
})
class SubmitButtonHost {
  readonly busy = signal(false);
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

  it('is disabled, aria-busy and shows the spinner when busy', async () => {
    const button = await render(true);

    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.querySelector('mat-progress-spinner')).not.toBeNull();
  });
});
