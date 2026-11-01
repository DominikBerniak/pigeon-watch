import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../../../core/resources/translate.pipe';
import { CriticalityBadge } from '../../../../shared/ui';
import {
  Sighting,
  sightingCriticalityLabel,
  sightingCriticalityMarker,
  sightingStatusLabel,
} from '../../sighting.types';
import { sightingAge } from '../sighting-age';

@Component({
  selector: 'li[app-sighting-row]',
  imports: [DatePipe, RouterLink, TranslatePipe, CriticalityBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sighting-row.html',
  styleUrl: './sighting-row.scss',
})
export class SightingRow {
  readonly sighting = input.required<Sighting>();
  readonly now = input.required<Date>();
  readonly linkState = input<object | undefined>(undefined);

  protected readonly age = computed(() => sightingAge(this.sighting().createdAt, this.now()));
  protected readonly statusLabel = sightingStatusLabel;
  protected readonly criticalityLabel = sightingCriticalityLabel;
  protected readonly criticalityKind = sightingCriticalityMarker;
}
