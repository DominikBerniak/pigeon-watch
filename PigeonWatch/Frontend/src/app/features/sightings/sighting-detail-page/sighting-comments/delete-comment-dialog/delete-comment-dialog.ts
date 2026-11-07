import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe } from '../../../../../core/resources/translate.pipe';

@Component({
  selector: 'app-delete-comment-dialog',
  imports: [MatButtonModule, MatDialogModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './delete-comment-dialog.html',
})
export class DeleteCommentDialog {}
