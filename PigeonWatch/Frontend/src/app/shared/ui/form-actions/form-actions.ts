import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-form-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './form-actions.html',
  styleUrl: './form-actions.scss',
})
export class FormActions {}
