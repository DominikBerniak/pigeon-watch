import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '../../../core/resources/translate.pipe';
import { CriticalityBadge, provideFormFieldDefaults } from '../../../shared/ui';
import {
  SightingCriticality,
  SightingFilters,
  SightingStatus,
  filterableSightingStatuses,
  selectableSightingCriticalities,
  sightingCriticalityLabel,
  sightingCriticalityMarker,
  sightingStatusLabel,
} from '../sighting.types';

@Component({
  selector: 'app-nearby-map-filters',
  imports: [MatButtonModule, MatFormFieldModule, MatSelectModule, TranslatePipe, CriticalityBadge],
  providers: [provideFormFieldDefaults()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './nearby-map-filters.html',
  styleUrl: './nearby-map-filters.scss',
})
export class NearbyMapFilters {
  readonly disabled = input(false);
  readonly filtersChange = output<SightingFilters>();

  protected readonly statusOptions = filterableSightingStatuses;
  protected readonly criticalityOptions = selectableSightingCriticalities;
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly criticalityLabel = sightingCriticalityLabel;
  protected readonly criticalityKind = sightingCriticalityMarker;
  protected readonly statuses = signal<readonly SightingStatus[]>([]);
  protected readonly criticalities = signal<readonly SightingCriticality[]>([]);
  protected readonly active = computed(
    () => this.statuses().length > 0 || this.criticalities().length > 0,
  );

  protected clear(): void {
    if (this.disabled() || !this.active()) return;

    this.statuses.set([]);
    this.criticalities.set([]);
    this.emit();
  }

  protected changeStatuses(statuses: readonly SightingStatus[]): void {
    this.statuses.set(statuses);
    this.emit();
  }

  protected changeCriticalities(criticalities: readonly SightingCriticality[]): void {
    this.criticalities.set(criticalities);
    this.emit();
  }

  private emit(): void {
    this.filtersChange.emit({ statuses: this.statuses(), criticalities: this.criticalities() });
  }
}
