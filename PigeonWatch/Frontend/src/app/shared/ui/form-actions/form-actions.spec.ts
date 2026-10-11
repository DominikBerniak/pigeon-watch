import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormActions } from './form-actions';

@Component({
  imports: [FormActions],
  template: `<app-form-actions>
    <button type="button" class="first">Cancel</button>
    <button type="submit" class="second">Save</button>
  </app-form-actions>`,
})
class FormActionsHost {}

describe('FormActions', () => {
  it('projects both children in authored order and adds no text of its own', async () => {
    const fixture = TestBed.createComponent(FormActionsHost);
    await fixture.whenStable();
    const container: HTMLElement = fixture.nativeElement.querySelector('.form-actions');
    const children = Array.from(container.children);

    expect(children.map((child) => child.className)).toEqual(['first', 'second']);
    expect(container.textContent).toBe('CancelSave');
  });

  it('is a full-width block', async () => {
    const fixture = TestBed.createComponent(FormActionsHost);
    await fixture.whenStable();
    const host: HTMLElement = fixture.nativeElement.querySelector('app-form-actions');
    const style = getComputedStyle(host);

    expect(style.display).toBe('block');
    expect(style.width).toBe('100%');
  });
});
