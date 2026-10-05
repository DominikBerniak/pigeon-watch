import { ChangeDetectionStrategy, Component, input } from '@angular/core';

export type BrandSize = 'md' | 'lg';

@Component({
  selector: 'app-brand',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './brand.html',
  styleUrl: './brand.scss',
  host: {
    '[class.brand--lg]': "size() === 'lg'",
  },
})
export class Brand {
  readonly name = input.required<string>();
  readonly size = input<BrandSize>('md');
}
