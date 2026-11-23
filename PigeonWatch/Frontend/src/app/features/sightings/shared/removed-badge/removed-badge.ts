import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '../../../../core/resources/translate.pipe';

@Component({
  selector: 'app-removed-badge',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './removed-badge.html',
  styleUrl: './removed-badge.scss',
})
export class RemovedBadge {}
