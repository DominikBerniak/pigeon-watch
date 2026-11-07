import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSelectModule } from '@angular/material/select';
import { lastValueFrom } from 'rxjs';
import { choose } from './cell-driver';

@Component({
  imports: [MatSelectModule],
  template: `
    <mat-select class="first" multiple aria-label="First">
      <mat-option value="a">Alpha</mat-option>
    </mat-select>
    <mat-select class="second" multiple aria-label="Second">
      <mat-option value="b">Bravo</mat-option>
    </mat-select>
  `,
})
class TwoSelectsHost {}

describe('cell driver choose', () => {
  it('closes the panel of the select it chose from even while another select panel is open', async () => {
    const fixture = TestBed.createComponent(TwoSelectsHost);
    const root = fixture.nativeElement as HTMLElement;

    fixture.autoDetectChanges();
    await fixture.whenStable();

    const first = root.querySelector<HTMLElement>('mat-select.first')!;
    const second = root.querySelector<HTMLElement>('mat-select.second')!;

    first.querySelector<HTMLElement>('[cdk-overlay-origin]')?.click();
    await vi.waitFor(() => {
      expect(first.getAttribute('aria-expanded')).toBe('true');
      expect(document.querySelectorAll('.cdk-overlay-backdrop-showing')).toHaveLength(1);
    });

    await lastValueFrom(choose('mat-select.second', 'bravo')({ root, text: () => 'Bravo' }));
    await fixture.whenStable();

    await vi.waitFor(() => expect(second.getAttribute('aria-expanded')).toBe('false'));
    expect(second.textContent).toContain('Bravo');
    expect(first.getAttribute('aria-expanded')).toBe('true');
  });
});
