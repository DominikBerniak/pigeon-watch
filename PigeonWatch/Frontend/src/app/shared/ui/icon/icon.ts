import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type IconName = 'mail' | 'user' | 'report';

export type IconSize = 'md' | 'lg';

@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './icon.html',
  styleUrl: './icon.scss',
  host: {
    'aria-hidden': 'true',
    '[class.icon--lg]': "size() === 'lg'",
  },
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input<IconSize>('md');
}
