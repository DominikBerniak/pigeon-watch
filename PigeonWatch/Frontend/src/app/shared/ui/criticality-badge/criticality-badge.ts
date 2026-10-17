import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { MapMarkerKind } from '../../map/map-marker';

@Component({
  selector: 'app-criticality-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './criticality-badge.html',
  styleUrl: './criticality-badge.scss',
  host: {
    '[class.criticality-badge--monitor]': "kind() === 'monitor'",
    '[class.criticality-badge--needs-help-soon]': "kind() === 'needsHelpSoon'",
    '[class.criticality-badge--urgent]': "kind() === 'urgent'",
  },
})
export class CriticalityBadge {
  readonly kind = input.required<MapMarkerKind>();
}
