import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
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

  it('does not display the footer when nothing is projected into actions', async () => {
    const fixture = TestBed.createComponent(SubtitledPageCardHost);
    await fixture.whenStable();
    const footer: HTMLElement = fixture.nativeElement.querySelector('mat-card-actions');

    expect(getComputedStyle(footer).display).toBe('none');
  });

  it('displays the footer when actions are projected', async () => {
    const fixture = TestBed.createComponent(PageCardHost);
    await fixture.whenStable();
    const footer: HTMLElement = fixture.nativeElement.querySelector('mat-card-actions');

    expect(getComputedStyle(footer).display).not.toBe('none');
  });

  it('moves focus to the h1 through focusHeading() without making it a Tab stop', async () => {
    const fixture = TestBed.createComponent(PageCardHost);
    document.body.appendChild(fixture.nativeElement);
    await fixture.whenStable();
    const heading: HTMLElement = fixture.nativeElement.querySelector('h1');
    const card: PageCard = fixture.debugElement.query(By.directive(PageCard)).componentInstance;

    card.focusHeading();

    expect(document.activeElement).toBe(heading);
    expect(heading.tabIndex).toBe(-1);
    expect(heading.getAttribute('tabindex')).toBe('-1');

    fixture.nativeElement.remove();
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
