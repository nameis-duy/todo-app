import { Routes } from '@angular/router';
import { TaskWorkspace } from './tasks/task-workspace';
import { authGuard } from './auth/auth-service';
import { SignIn } from './auth/sign-in';

export const routes: Routes = [
	{ path: 'login', component: SignIn, title: 'Sign In | Todo' },
	{ path: '', component: TaskWorkspace, canActivate: [authGuard], title: 'Tasks & Projects | Todo' },
	{ path: 'purposes', redirectTo: '', pathMatch: 'full' },
	{ path: '**', redirectTo: '' },
];
