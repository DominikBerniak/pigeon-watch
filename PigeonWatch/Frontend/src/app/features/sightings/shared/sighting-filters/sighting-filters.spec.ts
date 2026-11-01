import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import {
  SightingCriticality,
  SightingFilters,
  SightingStatus,
  allSightingStatuses,
  filterableSightingStatuses,
} from '../../sighting.types';
import { SightingFilterRow } from './sighting-filters';

@Component({
  imports: [SightingFilterRow],
  template: `<app-sighting-filters
    [statusOptions]="statusOptions"
    [disabled]="disabled()"
    (filtersChange)="emitted.push($event)"
  />`,
})
class SightingFiltersHost {
  readonly statusOptions = filterableSightingStatuses;
  readonly disabled = signal(false);
  readonly emitted: SightingFilters[] = [];
}

@Component({
  imports: [SightingFilterRow],
  template: `<app-sighting-filters
    [statusOptions]="statusOptions"
    [initial]="initial"
    (filtersChange)="emitted.push($event)"
  />`,
})
class SightingFiltersWithInitialHost {
  readonly statusOptions = allSightingStatuses;
  readonly initial: SightingFilters = {
    statuses: [SightingStatus.adopted],
    criticalities: [SightingCriticality.urgent],
  };
  readonly emitted: SightingFilters[] = [];
}

describe('SightingFilterRow', () => {
  let fixture: ComponentFixture<SightingFiltersHost>;
  let loader: HarnessLoader;

  beforeEach(async () => {
    fixture = TestBed.createComponent(SightingFiltersHost);
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

  it('offers the statuses the host passes, here the five open ones in workflow order', async () => {
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
    return root().querySelector<HTMLButtonElement>('.sighting-filters__clear')!;
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

describe('SightingFilterRow with all statuses and an initial selection', () => {
  let fixture: ComponentFixture<SightingFiltersWithInitialHost>;
  let loader: HarnessLoader;

  beforeEach(async () => {
    fixture = TestBed.createComponent(SightingFiltersWithInitialHost);
    loader = TestbedHarnessEnvironment.loader(fixture);
    await fixture.whenStable();
  });

  function clearButton(): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.sighting-filters__clear',
    )!;
  }

  it('offers all nine statuses in workflow order', async () => {
    const [status] = await loader.getAllHarnesses(MatSelectHarness);

    await status.open();

    const labels = await Promise.all((await status.getOptions()).map((option) => option.getText()));

    await status.close();

    expect(labels).toEqual([
      'Spotted',
      'Caught',
      'At the vet',
      'In home care',
      'Awaiting adoption',
      'Adopted',
      'Released',
      'Passed away',
      'Not found',
    ]);
  });

  it('starts from the initial selection without emitting it', async () => {
    const [status, criticality] = await loader.getAllHarnesses(MatSelectHarness);

    expect(await status.getValueText()).toBe('Adopted');
    expect(await criticality.getValueText()).toBe('Urgent');
    expect(clearButton().hasAttribute('aria-disabled')).toBe(false);
    expect(fixture.componentInstance.emitted).toEqual([]);
  });

  it('changes and clears from the initial selection', async () => {
    const [status, criticality] = await loader.getAllHarnesses(MatSelectHarness);

    await criticality.open();
    await criticality.clickOptions({ text: 'Monitor' });
    await criticality.close();

    expect(fixture.componentInstance.emitted.at(-1)).toEqual({
      statuses: [SightingStatus.adopted],
      criticalities: [SightingCriticality.monitor, SightingCriticality.urgent],
    });

    clearButton().click();
    await fixture.whenStable();

    expect(fixture.componentInstance.emitted.at(-1)).toEqual({ statuses: [], criticalities: [] });
    expect(await status.getValueText()).toBe('All');
  });
});
