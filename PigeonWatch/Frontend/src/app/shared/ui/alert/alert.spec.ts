import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Alert, AlertKind } from './alert';

@Component({
  imports: [Alert],
  template: `<app-alert [kind]="kind()">Projected message</app-alert>`,
})
class AlertHost {
  readonly kind = signal<AlertKind>('error');
}

describe('Alert', () => {
  it.each([
    ['error', 'alert'],
    ['info', 'status'],
    ['success', 'status'],
  ] as const)('renders %s with role %s and the projected text', async (kind, role) => {
    const fixture = TestBed.createComponent(AlertHost);
    fixture.componentInstance.kind.set(kind);
    await fixture.whenStable();

    const alert: HTMLElement = fixture.nativeElement.querySelector('app-alert');
    expect(alert.getAttribute('role')).toBe(role);
    expect(alert.classList).toContain(`alert--${kind}`);
    expect(alert.textContent?.trim()).toBe('Projected message');
  });
});
