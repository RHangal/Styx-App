import { Routes } from '@angular/router';
import { ProfilePageComponent } from './profile-page/profile-page.component';

import { AuthGuard } from './authguard';
import { HomePageComponent } from './home-page/home-page.component';

import { ShopPageComponent } from './shop-page/shop-page.component';
import { UnverifiedPageComponent } from './unverified-page/unverified-page.component';
import { EmailAuthGuard } from './email.authguard';
import { PostsComponent } from './habit-pages/posts.component';

export const routes: Routes = [
  { path: '', component: HomePageComponent }, // Public route
  {
    path: 'habits/:postType',
    component: PostsComponent,
    // canActivate: [AuthGuard],
  },
  {
    path: 'shop',
    component: ShopPageComponent,
    // canActivate: [AuthGuard], // Protect route with AuthGuard
  },
  {
    path: 'profile',
    component: ProfilePageComponent,
    // canActivate: [AuthGuard], // Protect route with AuthGuard
  },
  // {
  //   path: 'unverified',
  //   component: UnverifiedPageComponent,
  // },
  {
    path: '**',
    redirectTo: '', // Default redirect to home
    pathMatch: 'full',
  },
];
