import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { SightingCriticality, SightingFilters, SightingStatus } from '../sighting.types';
import { NearbyMapFilters } from './nearby-map-filters';

@Component({
  imports: [NearbyMapFilters],
  template: `<app-nearby-map-filters
    [disabled]="disabled()"
    (filtersChange)="emitted.push($event)"
  />`,
})
class NearbyMapFiltersHost {
  readonly disabled = signal(false);
  readonly emitted: SightingFilters[] = [];
}

describe('NearbyMapFilters', () => {
  let fixture: ComponentFixture<NearbyMapFiltersHost>;
  let loader: HarnessLoader;

  beforeEach(async () => {
    fixture = TestBed.createComponent(NearbyMapFiltersHost);
    loader = TestbedHarnessEnvironment.loader(fixture);
    await fixture.whenStable();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function selects(): Promise<MatSelectHarness[]> {
    return loader.getAllHarnesses(MatSelectHarness);
  }

  async function optionLabels(harness: MatSelectHarness): Promise<string[]> {
    await harness.open();

    const labels = await Promise.all(
      (await harness.getOptions()).map((option) => option.getText()),
    );

    await harness.close();

    return labels;
  }

  function emitted(): SightingFilters[] {
    return fixture.componentInstance.emitted;
  }

  it('groups the two fields under the filter label with their own labels', () => {
    const group = root().querySelector('[role="group"]');

    expect(group?.getAttribute('aria-label')).toBe('Filter sightings');
    expect(
      [...root().querySelectorAll('mat-label')].map((label) => label.textContent?.trim()),
    ).toEqual(['Status', 'Criticality']);
  });

  it('shows the "all" placeholder in both multi-selects while nothing is selected', async () => {
    const [status, criticality] = await selects();

    expect(await status.isMultiple()).toBe(true);
    expect(await criticality.isMultiple()).toBe(true);
    expect(await status.getValueText()).toBe('All');
    expect(await criticality.getValueText()).toBe('All');
    expect(emitted()).toEqual([]);
  });

  it('offers the five open statuses in workflow order', async () => {
    const [status] = await selects();

    expect(await optionLabels(status)).toEqual([
      'Spotted',
      'Caught',
      'At the vet',
      'In home care',
      'Awaiting adoption',
    ]);
  });

  it('offers the three criticalities, each with its badge', async () => {
    const [, criticality] = await selects();

    expect(await optionLabels(criticality)).toEqual(['Monitor', 'Needs help soon', 'Urgent']);

    await criticality.open();

    expect(document.querySelectorAll('mat-option app-criticality-badge')).toHaveLength(3);

    await criticality.close();
  });

  it('emits both arrays each time a selection changes', async () => {
    const [status, criticality] = await selects();

    await criticality.open();
    await criticality.clickOptions({ text: 'Urgent' });

    expect(emitted()).toEqual([{ statuses: [], criticalities: [SightingCriticality.urgent] }]);

    await criticality.clickOptions({ text: 'Monitor' });
    await criticality.close();

    expect(emitted().at(-1)).toEqual({
      statuses: [],
      criticalities: [SightingCriticality.monitor, SightingCriticality.urgent],
    });
    expect(await criticality.getValueText()).toBe('Monitor, Urgent');

    await status.open();
    await status.clickOptions({ text: 'Caught' });
    await status.close();

    expect(emitted().at(-1)).toEqual({
      statuses: [SightingStatus.caught],
      criticalities: [SightingCriticality.monitor, SightingCriticality.urgent],
    });
  });

  it('emits empty arrays and shows the placeholder again once a selection is cleared', async () => {
    const [, criticality] = await selects();

    await criticality.open();
    await criticality.clickOptions({ text: 'Urgent' });
    await criticality.clickOptions({ text: 'Urgent' });
    await criticality.close();

    expect(emitted().at(-1)).toEqual({ statuses: [], criticalities: [] });
    expect(await criticality.getValueText()).toBe('All');
  });

  function clearButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('.nearby-filters__clear')!;
  }

  it('keeps Clear filters disabled while nothing is selected', () => {
    expect(clearButton().textContent?.trim()).toBe('Clear filters');
    expect(clearButton().getAttribute('aria-disabled')).toBe('true');

    clearButton().click();

    expect(emitted()).toEqual([]);
  });

  it('clears both selections with one emission when Clear filters is pressed', async () => {
    const [status, criticality] = await selects();

    await status.open();
    await status.clickOptions({ text: 'Caught' });
    await status.close();
    await criticality.open();
    await criticality.clickOptions({ text: 'Urgent' });
    await criticality.close();

    expect(clearButton().hasAttribute('aria-disabled')).toBe(false);

    const before = emitted().length;
    clearButton().click();
    await fixture.whenStable();

    expect(emitted().slice(before)).toEqual([{ statuses: [], criticalities: [] }]);
    expect(await status.getValueText()).toBe('All');
    expect(await criticality.getValueText()).toBe('All');
    expect(clearButton().getAttribute('aria-disabled')).toBe('true');
  });

  it('disables Clear filters while the host disables the filters', async () => {
    const [, criticality] = await selects();

    await criticality.open();
    await criticality.clickOptions({ text: 'Urgent' });
    await criticality.close();
    fixture.componentInstance.disabled.set(true);
    await fixture.whenStable();

    const before = emitted().length;
    clearButton().click();

    expect(clearButton().getAttribute('aria-disabled')).toBe('true');
    expect(emitted()).toHaveLength(before);
  });

  it('disables both selects while the host says so', async () => {
    fixture.componentInstance.disabled.set(true);
    await fixture.whenStable();

    const [status, criticality] = await selects();

    expect(await status.isDisabled()).toBe(true);
    expect(await criticality.isDisabled()).toBe(true);

    fixture.componentInstance.disabled.set(false);
    await fixture.whenStable();

    expect(await status.isDisabled()).toBe(false);
  });
});
