import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PageCard } from './page-card';

@Component({
  imports: [PageCard],
  template: `<app-page-card [title]="'Card title'">
    <p class="body">Body</p>
    <div actions class="actions">Actions</div>
  </app-page-card>`,
})
class PageCardHost {}

@Component({
  imports: [PageCard],
  template: `<app-page-card [title]="'Card title'" [subtitle]="subtitle()" />`,
})
class SubtitledPageCardHost {
  readonly subtitle = signal<string | undefined>('Card subtitle');
}

describe('PageCard', () => {
  it('renders the title as the h1 and projects the body and actions', async () => {
    const fixture = TestBed.createComponent(PageCardHost);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelectorAll('h1').length).toBe(1);
    expect(element.querySelector('h1')?.textContent?.trim()).toBe('Card title');
    expect(element.querySelector('mat-card-content .body')).not.toBeNull();
    expect(element.querySelector('mat-card-actions .actions')).not.toBeNull();
  });

  it('renders the subtitle under the title only when one is given', async () => {
    const fixture = TestBed.createComponent(SubtitledPageCardHost);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('h1 + p')?.textContent?.trim()).toBe('Card subtitle');

    fixture.componentInstance.subtitle.set(undefined);
    await fixture.whenStable();

    expect(element.querySelector('h1 + p')).toBeNull();
  });
});
