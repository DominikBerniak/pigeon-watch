import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe } from '../../../../core/resources/translate.pipe';

export interface PhotoDialogData {
  readonly url: string;
  readonly title: string;
  readonly alt: string;
}

@Component({
  selector: 'app-photo-dialog',
  imports: [MatButtonModule, MatDialogModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './photo-dialog.html',
  styleUrl: './photo-dialog.scss',
})
export class PhotoDialog {
  protected readonly data = inject<PhotoDialogData>(MAT_DIALOG_DATA);
}
